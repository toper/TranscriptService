using System.ComponentModel.DataAnnotations;
using TranscriptService.Worker.Core;

namespace TranscriptService.Worker.FasterWhisper;

/// <summary>Section <c>Engine</c> for the FasterWhisper.NET worker.</summary>
public sealed class FasterWhisperOptions : EngineOptions
{
    /// <summary>
    /// CTranslate2 compute type. Empty picks <c>float16</c> on CUDA and <c>float32</c> on CPU;
    /// the CPU build of CTranslate2 shipped with FasterWhisper.NET has no efficient <c>int8</c>.
    /// </summary>
    public string? ComputeType { get; set; }

    /// <summary>Beam width; 1 (greedy) keeps latency low for telephony, as in the ToperTriton setup.</summary>
    [Range(1, 10)]
    public int BeamSize { get; set; } = 1;

    /// <summary>CPU threads used by CTranslate2.</summary>
    [Range(1, 256)]
    public int CpuThreads { get; set; } = Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>
    /// Silero voice activity detection. Off by default: with FasterWhisper.NET 1.0.8 it discards
    /// all speech in 8 kHz telephone recordings (measured), which is worse than no filtering.
    /// </summary>
    public bool VadEnabled { get; set; }

    public string EffectiveComputeType => string.IsNullOrWhiteSpace(ComputeType)
        ? Device == ComputeDevice.Cuda ? "float16" : "float32"
        : ComputeType;
}
