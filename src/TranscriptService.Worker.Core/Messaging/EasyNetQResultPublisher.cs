using EasyNetQ;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Worker.Core.Messaging;

/// <summary>Publishes job outcomes as events the API subscribes to.</summary>
internal sealed class EasyNetQResultPublisher(IBus bus) : ITranscriptionResultPublisher
{
    public Task PublishAsync(TranscriptionCompleted message, CancellationToken cancellationToken) =>
        bus.PubSub.PublishAsync(message, cancellationToken);

    public Task PublishAsync(TranscriptionFailed message, CancellationToken cancellationToken) =>
        bus.PubSub.PublishAsync(message, cancellationToken);
}
