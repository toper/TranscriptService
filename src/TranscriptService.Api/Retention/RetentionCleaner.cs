using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Options;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Retention;

/// <summary>
/// Keeps jobs short-lived: finished jobs older than the result TTL are deleted with their recording,
/// and jobs no worker finished in time are failed so the client hears about it.
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

    /// <returns>The number of deleted jobs.</returns>
    public async Task<int> CleanAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();

        var expiresBefore = now - settings.ResultTtl;
        var expired = await db.Jobs
            .Where(j => j.Status != JobStatus.Queued && j.FinishedAt < expiresBefore)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        db.Jobs.RemoveRange(expired);

        var staleBefore = now - (settings.MaxAudioDuration * QueuedTimeoutFactor);
        var stale = await db.Jobs
            .Where(j => j.Status == JobStatus.Queued && j.CreatedAt < staleBefore)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var job in stale)
        {
            job.Status = JobStatus.Failed;
            job.FailureReason = "The transcription timed out: no engine worker finished the job.";
            job.FinishedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var job in expired.Concat(stale))
        {
            storage.Delete(job.AudioPath);
        }

        foreach (var job in stale.Where(j => j.CallbackUrl is not null))
        {
            await callbacks.EnqueueAsync(job.Id, cancellationToken).ConfigureAwait(false);
        }

        if (expired.Count + stale.Count > 0)
        {
            LogCleaned(expired.Count, stale.Count);
        }

        return expired.Count;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention: deleted {Expired} expired jobs, timed out {Stale} queued jobs")]
    private partial void LogCleaned(int expired, int stale);
}
