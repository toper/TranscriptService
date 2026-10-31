namespace TranscriptService.Audio;

/// <summary>
/// The recording cannot be transcribed as-is. The message is meant for API clients,
/// so it names what was detected instead of silently converting it.
/// </summary>
public sealed class UnsupportedAudioException : Exception
{
    public UnsupportedAudioException(string message)
        : base(message)
    {
    }

    public UnsupportedAudioException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
