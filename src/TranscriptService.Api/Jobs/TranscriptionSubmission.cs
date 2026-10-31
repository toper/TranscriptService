using Microsoft.Extensions.Options;
using TranscriptService.Api.Messaging;
using TranscriptService.Api.Options;
using TranscriptService.Audio;
using TranscriptService.Contracts.Http;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Jobs;

/// <summary>
/// Turns an accepted upload into a queued job: store the recording, record the job, queue the
/// request. Each step undoes the previous ones on failure, so no recording outlives its job and no
/// job exists without a queued request.
/// </summary>
public sealed class TranscriptionSubmission(
    TranscriptionDbContext db,
    AudioStorage storage,
    ITranscriptionRequestPublisher publisher,
    IOptions<ApiOptions> options,
    TimeProvider time)
{
    /// <returns>The queued job, or <see langword="null"/> when the queue is unavailable and nothing was kept.</returns>
    public async Task<TranscriptionJob?> SubmitAsync(
        Stream audio,
        AudioFormatInfo format,
        string language,
        Uri? callbackUrl,
        CancellationToken cancellationToken)
    {
        var jobId = Guid.CreateVersion7();
        var job = new TranscriptionJob
        {
            Id = jobId,
            Status = JobStatus.Queued,
            Language = language,
            AudioPath = storage.PathFor(jobId),
            AudioDuration = format.Duration,
            CallbackUrl = callbackUrl,
            CreatedAt = time.GetUtcNow(),
        };

        try
        {
            await storage.SaveAsync(jobId, audio, cancellationToken).ConfigureAwait(false);
            db.Jobs.Add(job);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            storage.Delete(job.AudioPath);
            throw;
        }

        try
        {
            var priority = MessagePriority.For(format.Duration, options.Value.HighPriorityThreshold);
            await publisher.PublishAsync(new TranscribeAudio(jobId, job.AudioPath, language, format.Duration), priority, cancellationToken).ConfigureAwait(false);
            return job;
        }
#pragma warning disable CA1031 // Any publishing failure means the job cannot run; undo it and report the queue as unavailable.
        catch (Exception)
#pragma warning restore CA1031
        {
            db.Jobs.Remove(job);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            storage.Delete(job.AudioPath);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return null;
        }
    }
}
