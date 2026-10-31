using Microsoft.Extensions.Hosting;

namespace TranscriptService.Worker.Core.Health;

/// <summary>
/// Loads the model while the host is starting, before any hosted service consumes messages.
/// An exception here aborts startup, which is the point: a worker with a missing model or
/// unavailable CUDA must not report itself as running.
/// </summary>
public sealed class ModelInitializationService(ITranscriber transcriber, ModelReadiness readiness) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await transcriber.InitializeAsync(cancellationToken).ConfigureAwait(false);
        readiness.MarkReady();
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
