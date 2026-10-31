using System.ComponentModel.DataAnnotations;

namespace TranscriptService.Worker.Core;

/// <summary>Where the speech engine runs.</summary>
public enum ComputeDevice
{
    Cpu,
    Cuda,
}

/// <summary>
/// Configuration section <c>Engine</c>, common to every engine worker. Engines extend it with
/// their own settings. Validated at startup: a wrong deployment fails fast, not on the first job.
/// </summary>
public class EngineOptions
{
    public const string SectionName = "Engine";

    /// <summary>Model file or directory inside the read-only <c>/models</c> volume.</summary>
    [Required]
    public string ModelPath { get; set; } = string.Empty;

    public ComputeDevice Device { get; set; } = ComputeDevice.Cpu;
}
