using EasyNetQ;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Messaging;

/// <summary>Subscribes to result events from the engine workers and records them, one scope per message.</summary>
internal sealed class TranscriptionResultSubscriber(IBus bus, IServiceScopeFactory scopes) : BackgroundService
{
    private const string SubscriptionId = "transcript-api";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var completed = await bus.PubSub.SubscribeAsync<TranscriptionCompleted>(
                SubscriptionId,
                (message, cancellationToken) => HandleAsync(handler => handler.HandleAsync(message, cancellationToken)),
                static _ => { },
                stoppingToken).ConfigureAwait(false);
            await using (completed.ConfigureAwait(false))
            {
                var failed = await bus.PubSub.SubscribeAsync<TranscriptionFailed>(
                    SubscriptionId,
                    (message, cancellationToken) => HandleAsync(handler => handler.HandleAsync(message, cancellationToken)),
                    static _ => { },
                    stoppingToken).ConfigureAwait(false);
                await using (failed.ConfigureAwait(false))
                {
                    await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown: subscriptions are disposed above.
        }
    }

    private async Task HandleAsync(Func<TranscriptionResultHandler, Task> handle)
    {
        await using var scope = scopes.CreateAsyncScope();
        await handle(scope.ServiceProvider.GetRequiredService<TranscriptionResultHandler>()).ConfigureAwait(false);
    }
}
