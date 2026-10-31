using TranscriptService.Worker.Core;
using TranscriptService.Worker.FasterWhisper;

return await WorkerHost.RunAsync<FasterWhisperTranscriber, FasterWhisperOptions>(args).ConfigureAwait(false);
