using Microsoft.Extensions.Logging;
using TranscriptService.Audio;
using TranscriptService.Contracts.Messages;
using TranscriptService.Worker.Core.Messaging;

namespace TranscriptService.Worker.Core;

/// <summary>
/// Processes one <see cref="TranscribeAudio"/> command: prepares the recording, runs the engine and
/// publishes the outcome. A recording that cannot be transcribed becomes a
/// <see cref="TranscriptionFailed"/> event, so the message is acknowledged instead of redelivered
/// forever. Host shutdown and a failure to publish propagate, and the broker redelivers the job.
/// </summary>
public sealed partial class TranscriptionJobHandler(
    ITranscriber transcriber,
    ITranscriptionResultPublisher publisher,
    TimeProvider timeProvider,
    ILogger<TranscriptionJobHandler> logger)
{
    private const string EngineFailureReason = "The speech engine failed to transcribe the recording.";
    private const string MissingRecordingReason = "The recording was not found on the shared audio volume.";

    public async Task HandleAsync(TranscribeAudio command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var started = timeProvider.GetTimestamp();
        TranscriptionCompleted completed;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var audio = await AudioPreprocessor.LoadAsync(command.AudioPath, cancellationToken).ConfigureAwait(false);
            var output = await transcriber.TranscribeAsync(audio, command.Language, cancellationToken).ConfigureAwait(false);
            completed = new TranscriptionCompleted(
                command.JobId,
                transcriber.EngineName,
                output.FullText,
                output.Segments,
                audio.Duration,
                timeProvider.GetElapsedTime(started));
        }
#pragma warning disable CA1031 // Any engine fault must end the job with a failure event rather than poison the queue.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            var failed = new TranscriptionFailed(command.JobId, transcriber.EngineName, ReasonFor(ex, command.JobId));
            await publisher.PublishAsync(failed, cancellationToken).ConfigureAwait(false);
            return;
        }

        LogCompleted(command.JobId, completed.AudioDuration, completed.ProcessingTime);
        await publisher.PublishAsync(completed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Client-facing reason for a failed job. Rejections explain themselves; anything else is an
    /// engine fault whose details stay in the logs.
    /// </summary>
    private string ReasonFor(Exception exception, Guid jobId)
    {
        var rejection = exception switch
        {
            UnsupportedAudioException or TranscriptionRejectedException => exception.Message,
            FileNotFoundException or DirectoryNotFoundException => MissingRecordingReason,
            _ => null,
        };

        if (rejection is null)
        {
            LogEngineFailure(exception, jobId);
            return EngineFailureReason;
        }

        LogRejected(jobId, exception.Message);
        return rejection;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Transcribed job {JobId}: {AudioDuration} of audio in {ProcessingTime}")]
    private partial void LogCompleted(Guid jobId, TimeSpan audioDuration, TimeSpan processingTime);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected job {JobId}: {Reason}")]
    private partial void LogRejected(Guid jobId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine failed on job {JobId}")]
    private partial void LogEngineFailure(Exception exception, Guid jobId);
}
