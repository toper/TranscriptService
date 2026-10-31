namespace TranscriptService.Audio;

/// <summary>Sample encodings accepted for transcription — the ones telephony systems record.</summary>
public enum AudioEncoding
{
    /// <summary>Linear PCM, 16 bits per sample.</summary>
    Pcm16,

    /// <summary>G.711 μ-law, 8 bits per sample (North America, Japan).</summary>
    MuLaw,

    /// <summary>G.711 A-law, 8 bits per sample (Europe).</summary>
    ALaw,
}
