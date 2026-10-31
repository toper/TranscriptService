using Microsoft.Extensions.Options;
using TranscriptService.Api.Options;

namespace TranscriptService.Api.Retention;

/// <summary>Runs <see cref="RetentionCleaner"/> every <see cref="ApiOptions.CleanupInterval"/>.</summary>
internal sealed partial class RetentionCleanupService(
    IServiceScopeFactory scopes,
    IOptions<ApiOptions> options,
    TimeProvider time,
    ILogger<RetentionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.CleanupInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await CleanOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private async Task CleanOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RetentionCleaner>().CleanAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // One failed run (e.g. a locked database) must not stop future cleanups.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            LogCleanupFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Retention cleanup failed; retrying at the next interval")]
    private partial void LogCleanupFailed(Exception exception);
}
