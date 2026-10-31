using System.ComponentModel.DataAnnotations;

namespace TranscriptService.Api.Options;

/// <summary>Configuration section <c>Api</c>, validated at startup.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Key clients send in the <c>X-API-KEY</c> header.</summary>
    [Required]
    [MinLength(16)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Shared secret for the HMAC-SHA256 signature of callbacks.</summary>
    [Required]
    [MinLength(32)]
    public string CallbackSigningSecret { get; set; } = string.Empty;

    /// <summary>Directory on the volume shared with the engine workers.</summary>
    [Required]
    public string StoragePath { get; set; } = "/data/audio";

    [Range(1, long.MaxValue)]
    public long MaxUploadBytes { get; set; } = 100L * 1024 * 1024;

    public TimeSpan MaxAudioDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a finished job stays available without a callback.</summary>
    public TimeSpan ResultTtl { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Recordings up to this length are queued with high priority.</summary>
    public TimeSpan HighPriorityThreshold { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
}
