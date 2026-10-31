using EasyNetQ;
using EasyNetQ.Persistent;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TranscriptService.ServiceDefaults;

/// <summary>Healthy while the bus keeps its connections to the broker.</summary>
internal sealed class RabbitMqHealthCheck(IBus bus) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var producer = bus.Advanced.GetConnectionStatus(PersistentConnectionType.Producer);
        var consumer = bus.Advanced.GetConnectionStatus(PersistentConnectionType.Consumer);

        // A connection that was never needed stays uninitialized; only a lost one is a problem.
        var disconnected = producer.State == PersistentConnectionState.Disconnected
            || consumer.State == PersistentConnectionState.Disconnected;
        return Task.FromResult(disconnected
            ? HealthCheckResult.Unhealthy($"RabbitMQ disconnected: {producer.FailureReason ?? consumer.FailureReason}")
            : HealthCheckResult.Healthy());
    }
}
