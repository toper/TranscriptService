using EasyNetQ;

namespace TranscriptService.Worker.Core.Messaging;

/// <summary>Publishes job outcomes as events the API subscribes to.</summary>
internal sealed class EasyNetQResultPublisher(IBus bus) : ITranscriptionResultPublisher
{
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class =>
        bus.PubSub.PublishAsync(message, cancellationToken);
}
