using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TranscriptService.Audio;

namespace TranscriptService.Worker.Core.Health;

/// <summary>
/// Loads the model in the background, so the health endpoints answer during a long warm-up
/// (<c>/health/live</c> OK, <c>/health/ready</c> not yet). A load failure stops the host with a
/// non-zero exit code: a worker with a missing model or unavailable CUDA must not keep running.
/// Before reporting ready it transcribes one second of silence: the first run on a GPU pays
/// one-time kernel and library initialisation (measured: 18 s with FasterWhisper on CUDA), which
/// belongs to the worker's start, not to the first client's job.
/// </summary>
public sealed partial class ModelInitializationService(
    ITranscriber transcriber,
    ModelReadiness readiness,
    IHostApplicationLifetime lifetime,
    ILogger<ModelInitializationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            LogLoading(transcriber.EngineName);
            await transcriber.InitializeAsync(stoppingToken).ConfigureAwait(false);
            await WarmUpAsync(stoppingToken).ConfigureAwait(false);
            readiness.MarkReady();
            LogLoaded(transcriber.EngineName);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shut down while the model was loading.
        }
#pragma warning disable CA1031 // Every load failure is fatal for the worker; it is logged and stops the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogLoadFailed(ex, transcriber.EngineName);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }

    private async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var silence = new AudioSamples(new float[AudioPreprocessor.TargetSampleRate], AudioPreprocessor.TargetSampleRate);
        var language = transcriber.SupportedLanguages.Order(StringComparer.Ordinal).First();
        await transcriber.TranscribeAsync(silence, language, cancellationToken).ConfigureAwait(false);
        var elapsed = Stopwatch.GetElapsedTime(started);
        LogWarmedUp(transcriber.EngineName, elapsed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Engine} warmed up in {Elapsed}")]
    private partial void LogWarmedUp(string engine, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading {Engine} model")]
    private partial void LogLoading(string engine);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Engine} model loaded; accepting jobs")]
    private partial void LogLoaded(string engine);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Failed to load the {Engine} model; stopping the worker")]
    private partial void LogLoadFailed(Exception exception, string engine);
}
