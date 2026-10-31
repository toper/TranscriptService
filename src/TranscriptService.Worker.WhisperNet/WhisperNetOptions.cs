using System.ComponentModel.DataAnnotations;
using TranscriptService.Worker.Core;

namespace TranscriptService.Worker.WhisperNet;

/// <summary>Section <c>Engine</c> for the Whisper.net worker.</summary>
public sealed class WhisperNetOptions : EngineOptions
{
    /// <summary>CPU threads for decoding; on CUDA most work runs on the GPU.</summary>
    [Range(1, 256)]
    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount);
}
