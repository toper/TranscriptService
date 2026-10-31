using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Options;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Retention;

/// <summary>
/// Keeps jobs short-lived. Finished jobs whose result can no longer be delivered (no callback, or
/// delivery given up) are deleted after the result TTL; jobs still waiting for their callback are
/// left to the dispatcher. Jobs no worker finished in time are failed so the client hears about it,
/// and recordings that belong to no job (an aborted upload) are removed.
/// </summary>
public sealed partial class RetentionCleaner(
    TranscriptionDbContext db,
    AudioStorage storage,
    PendingCallbacks callbacks,
    IOptions<ApiOptions> options,
    TimeProvider time,
    ILogger<RetentionCleaner> logger)
{
    /// <summary>A queued job is given up after this many times the longest accepted recording.</summary>
    private const int QueuedTimeoutFactor = 4;

    private const string TimedOutReason = "The transcription timed out: no engine worker finished the job.";

    /// <returns>The number of deleted jobs.</returns>
    public async Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();

        // Recordings are deleted when a job finishes, so expired jobs are rows only.
        var expiresBefore = now - settings.ResultTtl;
        var expired = await db.Jobs
            .Where(j => j.FinishedAt < expiresBefore
                && (j.Status == JobStatus.CallbackFailed || (j.Status != JobStatus.Queued && j.CallbackUrl == null)))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        var timedOut = await FailStaleJobsAsync(now - (settings.MaxAudioDuration * QueuedTimeoutFactor), now, cancellationToken).ConfigureAwait(false);
        var orphans = await DeleteOrphanedRecordingsAsync(expiresBefore, cancellationToken).ConfigureAwait(false);

        if (expired + timedOut + orphans > 0)
        {
            LogCleaned(expired, timedOut, orphans);
        }

        return expired;
    }

    private async Task<int> FailStaleJobsAsync(DateTimeOffset createdBefore, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stale = await db.Jobs
            .Where(j => j.Status == JobStatus.Queued && j.CreatedAt < createdBefore)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var job in stale)
        {
            job.Fail(TimedOutReason, now);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var job in stale)
        {
            storage.Delete(job.AudioPath);
            if (job.CallbackUrl is not null)
            {
                await callbacks.EnqueueAsync(job.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        return stale.Count;
    }

    private async Task<int> DeleteOrphanedRecordingsAsync(DateTimeOffset writtenBefore, CancellationToken cancellationToken)
    {
        var inUse = (await db.Jobs
                .Where(j => j.Status == JobStatus.Queued)
                .Select(j => j.AudioPath)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = storage.EnumerateRecordings()
            .Where(path => !inUse.Contains(path) && File.GetLastWriteTimeUtc(path) < writtenBefore.UtcDateTime)
            .ToList();
        orphans.ForEach(storage.Delete);
        return orphans.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention: deleted {Expired} expired jobs, timed out {TimedOut} queued jobs, removed {Orphans} orphaned recordings")]
    private partial void LogCleaned(int expired, int timedOut, int orphans);
}
