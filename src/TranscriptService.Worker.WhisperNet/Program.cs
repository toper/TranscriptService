using TranscriptService.ServiceDefaults;
using TranscriptService.Worker.Core;
using TranscriptService.Worker.WhisperNet;

if (await HealthProbe.TryRunAsync(args).ConfigureAwait(false) is { } probeExitCode)
{
    return probeExitCode;
}

var builder = WebApplication.CreateSlimBuilder(args);
builder.AddTranscriptionWorker<WhisperNetTranscriber, WhisperNetOptions>();

var app = builder.Build();
app.MapDefaultEndpoints();

await app.RunAsync().ConfigureAwait(false);
return 0;
