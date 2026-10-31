using EasyNetQ;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Worker.Core.Messaging;

/// <summary>
/// Consumes the shared request queue. Whichever engine worker is deployed competes for the same
/// queue; the queue is declared with priorities so short recordings overtake long calls.
/// </summary>
public sealed class TranscriptionRequestConsumer(
    IBus bus,
    TranscriptionJobHandler handler,
    IOptions<EngineOptions> options) : BackgroundService
{
    private IAsyncDisposable? _subscription;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _subscription = await bus.SendReceive.ReceiveAsync<TranscribeAudio>(
            QueueNames.TranscriptionRequests,
            handler.HandleAsync,
            configure => configure
                .WithPrefetchCount((ushort)options.Value.MaxConcurrency)
                .WithMaxPriority(MessagePriority.Max),
            stoppingToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop taking new jobs first; the in-flight one is cancelled and redelivered by the broker.
        if (_subscription is not null)
        {
            await _subscription.DisposeAsync().ConfigureAwait(false);
            _subscription = null;
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
