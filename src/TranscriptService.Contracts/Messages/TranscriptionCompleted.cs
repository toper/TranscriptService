namespace TranscriptService.Contracts.Messages;

/// <summary>Event published by an engine worker after a successful transcription.</summary>
/// <param name="JobId">Identifier of the transcription job.</param>
/// <param name="Engine">Name of the engine that produced the transcript.</param>
/// <param name="FullText">The whole transcript as a single string.</param>
/// <param name="Segments">Timed fragments of the transcript.</param>
/// <param name="AudioDuration">Duration of the recording.</param>
/// <param name="ProcessingTime">Time the worker spent on preprocessing and recognition.</param>
public sealed record TranscriptionCompleted(
    Guid JobId,
    string Engine,
    string FullText,
    IReadOnlyList<TranscriptSegment> Segments,
    TimeSpan AudioDuration,
    TimeSpan ProcessingTime);
