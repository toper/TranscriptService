namespace TranscriptService.Contracts.Messages;

/// <summary>Event published by an engine worker when a recording cannot be transcribed.</summary>
/// <param name="JobId">Identifier of the transcription job.</param>
/// <param name="Engine">Name of the engine that rejected the job.</param>
/// <param name="Reason">Human-readable reason, safe to show to API clients.</param>
public sealed record TranscriptionFailed(Guid JobId, string Engine, string Reason);
