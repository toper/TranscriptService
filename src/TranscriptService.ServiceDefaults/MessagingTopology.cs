using EasyNetQ;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.ServiceDefaults;

/// <summary>
/// The one definition of the request queue. RabbitMQ rejects a redeclaration with different
/// arguments, so the API (which declares it before sending) and the workers (which declare it when
/// consuming) take the arguments from here.
/// </summary>
public static class MessagingTopology
{
    /// <summary>Queue arguments: a priority queue capped at <see cref="MessagePriority.Max"/>.</summary>
    public static IDictionary<string, object?> TranscriptionRequestArguments { get; } =
        new Dictionary<string, object?>().WithMaxPriority(MessagePriority.Max);

    public static Task DeclareTranscriptionRequestsAsync(this IAdvancedBus bus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bus);
        return bus.QueueDeclareAsync(
            QueueNames.TranscriptionRequests,
            configure =>
            {
                foreach (var (name, value) in TranscriptionRequestArguments)
                {
                    configure.WithArgument(name, value!);
                }
            },
            cancellationToken);
    }
}
