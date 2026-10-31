using TranscriptService.Contracts;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Jobs;

/// <summary>A transcription job as stored by the API, the only owner of job state.</summary>
public sealed class TranscriptionJob
{
    public Guid Id { get; init; }

    public JobStatus Status { get; set; }

    public required string Language { get; init; }

    public required string AudioPath { get; init; }

    public TimeSpan AudioDuration { get; set; }

    public Uri? CallbackUrl { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? Engine { get; set; }

    public string? FullText { get; set; }

    public IReadOnlyList<TranscriptSegment> Segments { get; set; } = [];

    public string? FailureReason { get; set; }

    public TimeSpan? ProcessingTime { get; set; }
}
