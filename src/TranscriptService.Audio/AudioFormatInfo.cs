namespace TranscriptService.Audio;

/// <summary>Format of a recording as read from its WAV header.</summary>
public sealed record AudioFormatInfo(AudioEncoding Encoding, int SampleRate, int Channels, TimeSpan Duration);
