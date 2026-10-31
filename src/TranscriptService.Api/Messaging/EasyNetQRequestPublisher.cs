using EasyNetQ;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Messaging;

/// <summary>Sends requests to the shared work queue consumed by whichever engine is deployed.</summary>
internal sealed class EasyNetQRequestPublisher(IBus bus) : ITranscriptionRequestPublisher
{
    public Task PublishAsync(TranscribeAudio command, byte priority, CancellationToken cancellationToken) =>
        bus.SendReceive.SendAsync(
            QueueNames.TranscriptionRequests,
            command,
            configure => configure.WithPriority(priority),
            cancellationToken);
}
