using System.ComponentModel.DataAnnotations;
using TranscriptService.Worker.Core;

namespace TranscriptService.Worker.WhisperNet;

/// <summary>Section <c>Engine</c> for the Whisper.net worker.</summary>
public sealed class WhisperNetOptions : EngineOptions
{
    /// <summary>
    /// CPU threads per transcription. Defaults to the core count capped at 8: more threads give
    /// diminishing returns in whisper.cpp, and on CUDA most of the work runs on the GPU anyway.
    /// </summary>
    [Range(1, 256)]
    public int Threads { get; set; } = Math.Clamp(Environment.ProcessorCount, 1, 8);
}
