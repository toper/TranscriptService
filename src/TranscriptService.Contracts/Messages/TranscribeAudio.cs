namespace TranscriptService.Contracts.Messages;

/// <summary>Command asking an engine worker to transcribe a stored recording.</summary>
/// <param name="JobId">Identifier of the transcription job owned by the API.</param>
/// <param name="AudioPath">Path of the recording on the volume shared by the API and workers.</param>
/// <param name="Language">Two-letter language code of the speech, e.g. <c>pl</c>.</param>
/// <param name="AudioDuration">Duration of the recording, measured by the API on upload.</param>
public sealed record TranscribeAudio(Guid JobId, string AudioPath, string Language, TimeSpan AudioDuration);
