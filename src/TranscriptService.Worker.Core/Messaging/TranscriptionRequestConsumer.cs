using EasyNetQ;
using Microsoft.Extensions.Hosting;
using TranscriptService.Contracts.Messages;
using TranscriptService.Worker.Core.Health;

namespace TranscriptService.Worker.Core.Messaging;

/// <summary>
/// Consumes the shared request queue once the model is loaded. Whichever engine worker is deployed
/// competes for the same queue, declared with priorities so short recordings overtake long calls.
/// Each worker holds one job at a time (prefetch 1): a buffered job could no longer be overtaken
/// by a higher-priority one or taken by an idle replica. Scale out with replicas, not prefetch.
/// </summary>
public sealed class TranscriptionRequestConsumer(
    IBus bus,
    TranscriptionJobHandler handler,
    ModelReadiness readiness) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await readiness.WaitAsync(stoppingToken).ConfigureAwait(false);

            var subscription = await bus.SendReceive.ReceiveAsync<TranscribeAudio>(
                QueueNames.TranscriptionRequests,
                handler.HandleAsync,
                configure => configure.WithPrefetchCount(1).WithMaxPriority(MessagePriority.Max),
                stoppingToken).ConfigureAwait(false);
            await using (subscription.ConfigureAwait(false))
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: the subscription is disposed above; an in-flight job is redelivered by the broker.
        }
    }
}
