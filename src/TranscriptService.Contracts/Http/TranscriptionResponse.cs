namespace TranscriptService.Contracts.Http;

/// <summary>
/// A transcription job as returned by <c>GET /api/v1/transcriptions/{id}</c> and posted to the
/// client's callback URL.
/// </summary>
/// <param name="Id">Job identifier.</param>
/// <param name="Status">Where the job is in its lifecycle.</param>
/// <param name="Language">Two-letter language code of the speech.</param>
/// <param name="CreatedAt">When the recording was accepted.</param>
/// <param name="FinishedAt">When the engine finished or rejected the job.</param>
/// <param name="Engine">Engine that processed the job (<c>whispernet</c>, <c>fasterwhisper</c> or <c>vosk</c>).</param>
/// <param name="FullText">The whole transcript.</param>
/// <param name="Segments">Timed fragments: phrases for Whisper engines, single words for Vosk.</param>
/// <param name="FailureReason">Why the recording could not be transcribed.</param>
/// <param name="AudioDuration">Duration of the recording.</param>
/// <param name="ProcessingTime">Time the engine worker spent on the job.</param>
public sealed record TranscriptionResponse(
    Guid Id,
    JobStatus Status,
    string Language,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    string? Engine,
    string? FullText,
    IReadOnlyList<TranscriptSegment> Segments,
    string? FailureReason,
    TimeSpan AudioDuration,
    TimeSpan? ProcessingTime);
