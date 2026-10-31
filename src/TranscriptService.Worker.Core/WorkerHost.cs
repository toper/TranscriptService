using Microsoft.AspNetCore.Builder;
using TranscriptService.ServiceDefaults;

namespace TranscriptService.Worker.Core;

/// <summary>The whole entry point of an engine worker: <c>return await WorkerHost.RunAsync&lt;MyEngine, MyOptions&gt;(args);</c></summary>
public static class WorkerHost
{
    /// <returns>The process exit code: non-zero when the model could not be loaded.</returns>
    public static async Task<int> RunAsync<TTranscriber, TEngineOptions>(string[] args)
        where TTranscriber : class, ITranscriber
        where TEngineOptions : EngineOptions
    {
        if (await HealthProbe.TryRunAsync(args).ConfigureAwait(false) is { } probeExitCode)
        {
            return probeExitCode;
        }

        var builder = WebApplication.CreateSlimBuilder(args);
        builder.AddTranscriptionWorker<TTranscriber, TEngineOptions>();

        var app = builder.Build();
        await using (app.ConfigureAwait(false))
        {
            app.MapDefaultEndpoints();
            await app.RunAsync().ConfigureAwait(false);
        }

        // Set by ModelInitializationService when the model fails to load.
        return Environment.ExitCode;
    }
}
