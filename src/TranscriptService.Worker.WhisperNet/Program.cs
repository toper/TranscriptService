using TranscriptService.Worker.Core;
using TranscriptService.Worker.WhisperNet;

return await WorkerHost.RunAsync<WhisperNetTranscriber, WhisperNetOptions>(args).ConfigureAwait(false);
