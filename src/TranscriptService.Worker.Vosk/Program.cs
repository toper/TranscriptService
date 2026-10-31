using TranscriptService.Worker.Core;
using TranscriptService.Worker.Vosk;

return await WorkerHost.RunAsync<VoskTranscriber, VoskOptions>(args).ConfigureAwait(false);
