using System.Threading.Channels;

namespace TranscriptService.Api.Callbacks;

/// <summary>Jobs whose result must be delivered to the client's callback URL.</summary>
public sealed class PendingCallbacks
{
    private readonly Channel<Guid> _jobs = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken) =>
        _jobs.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _jobs.Reader.ReadAllAsync(cancellationToken);
}
