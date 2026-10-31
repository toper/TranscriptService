using EasyNetQ;
using TranscriptService.Contracts.Messages;
using TranscriptService.ServiceDefaults;

namespace TranscriptService.Api.Messaging;

/// <summary>
/// Sends requests to the shared work queue consumed by whichever engine is deployed. The queue is
/// declared here too (<see cref="MessagingTopology"/>): messages sent to an undeclared queue are
/// dropped by the broker, which would lose every request made while no worker has started yet.
/// </summary>
internal sealed class EasyNetQRequestPublisher(IBus bus) : ITranscriptionRequestPublisher, IDisposable
{
    private readonly SemaphoreSlim _declareLock = new(1, 1);
    private volatile bool _declared;

    public async Task PublishAsync(TranscribeAudio command, byte priority, CancellationToken cancellationToken)
    {
        await EnsureQueueDeclaredAsync(cancellationToken).ConfigureAwait(false);
        await bus.SendReceive.SendAsync(
            QueueNames.TranscriptionRequests,
            command,
            configure => configure.WithPriority(priority),
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _declareLock.Dispose();

    private async Task EnsureQueueDeclaredAsync(CancellationToken cancellationToken)
    {
        if (_declared)
        {
            return;
        }

        await _declareLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_declared)
            {
                await bus.Advanced.DeclareTranscriptionRequestsAsync(cancellationToken).ConfigureAwait(false);
                _declared = true;
            }
        }
        finally
        {
            _declareLock.Release();
        }
    }
}
