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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnsupportedAudioException or TranscriptionRejectedException)
        {
            LogRejected(command.JobId, ex.Message);
            await PublishFailureAsync(command, ex.Message, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            LogRejected(command.JobId, ex.Message);
            await PublishFailureAsync(command, MissingRecordingReason, cancellationToken).ConfigureAwait(false);
            return;
        }
#pragma warning disable CA1031 // Any engine fault must end the job with a failure event rather than poison the queue.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogEngineFailure(ex, command.JobId);
            await PublishFailureAsync(command, EngineFailureReason, cancellationToken).ConfigureAwait(false);
            return;
        }

        LogCompleted(command.JobId, completed.AudioDuration, completed.ProcessingTime);
        await publisher.PublishAsync(completed, cancellationToken).ConfigureAwait(false);
    }

    private Task PublishFailureAsync(TranscribeAudio command, string reason, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new TranscriptionFailed(command.JobId, transcriber.EngineName, reason), cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Transcribed job {JobId}: {AudioDuration} of audio in {ProcessingTime}")]
    private partial void LogCompleted(Guid jobId, TimeSpan audioDuration, TimeSpan processingTime);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected job {JobId}: {Reason}")]
    private partial void LogRejected(Guid jobId, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Engine failed on job {JobId}")]
    private partial void LogEngineFailure(Exception exception, Guid jobId);
}
