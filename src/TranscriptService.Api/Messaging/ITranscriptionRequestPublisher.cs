using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Messaging;

/// <summary>Queues a transcription request for the engine workers.</summary>
public interface ITranscriptionRequestPublisher
{
    Task PublishAsync(TranscribeAudio command, byte priority, CancellationToken cancellationToken);
}
