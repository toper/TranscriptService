using TranscriptService.Contracts.Messages;

namespace TranscriptService.Worker.Core.Messaging;

/// <summary>Reports the outcome of a transcription job back to the API.</summary>
public interface ITranscriptionResultPublisher
{
    Task PublishAsync(TranscriptionCompleted message, CancellationToken cancellationToken);

    Task PublishAsync(TranscriptionFailed message, CancellationToken cancellationToken);
}
