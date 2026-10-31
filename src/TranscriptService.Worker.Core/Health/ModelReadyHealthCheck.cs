using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TranscriptService.Worker.Core.Health;

/// <summary>Keeps the worker out of rotation until its model is loaded.</summary>
public sealed class ModelReadyHealthCheck(ModelReadiness readiness) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(readiness.IsReady
            ? HealthCheckResult.Healthy("Model loaded.")
            : HealthCheckResult.Unhealthy("Model is still loading."));
}
