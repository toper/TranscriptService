namespace TranscriptService.Contracts;

/// <summary>
/// A timed fragment of a transcript. Whisper-based engines produce phrases;
/// Vosk produces single words.
/// </summary>
/// <param name="Text">Recognized text of the fragment.</param>
/// <param name="Start">Offset from the beginning of the recording.</param>
/// <param name="End">Offset at which the fragment ends.</param>
public sealed record TranscriptSegment(string Text, TimeSpan Start, TimeSpan End);
