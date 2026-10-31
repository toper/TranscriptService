using Microsoft.EntityFrameworkCore;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Jobs;
using TranscriptService.Contracts.Http;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Messaging;

/// <summary>
/// Records the outcome reported by an engine worker. Idempotent: events for unknown (deleted) or
/// already finished jobs are ignored, so broker redeliveries never trigger a second callback.
/// The recording is deleted as soon as it has been processed.
/// </summary>
public sealed partial class TranscriptionResultHandler(
    TranscriptionDbContext db,
    AudioStorage storage,
    PendingCallbacks callbacks,
    TimeProvider time,
    ILogger<TranscriptionResultHandler> logger)
{
    public Task HandleAsync(TranscriptionCompleted message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return FinishAsync(message.JobId, message.Engine, job =>
        {
            job.Status = JobStatus.Completed;
            job.FullText = message.FullText;
            job.Segments = message.Segments;
            job.AudioDuration = message.AudioDuration;
            job.ProcessingTime = message.ProcessingTime;
            job.FinishedAt = time.GetUtcNow();
        }, cancellationToken);
    }

    public Task HandleAsync(TranscriptionFailed message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return FinishAsync(message.JobId, message.Engine, job => job.Fail(message.Reason, time.GetUtcNow()), cancellationToken);
    }

    private async Task FinishAsync(Guid jobId, string engine, Action<TranscriptionJob> apply, CancellationToken cancellationToken)
    {
        var job = await db.Jobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status != JobStatus.Queued)
        {
            LogIgnored(jobId, job?.Status);
            return;
        }

        apply(job);
        job.Engine = engine;
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Deleted by the client while the worker was transcribing.
            LogIgnored(jobId, status: null);
            return;
        }

        storage.Delete(job.AudioPath);

        if (job.CallbackUrl is not null)
        {
            await callbacks.EnqueueAsync(job.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ignoring result for job {JobId} in state {Status} (deleted or already finished)")]
    private partial void LogIgnored(Guid jobId, JobStatus? status);
}
