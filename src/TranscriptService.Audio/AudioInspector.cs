using NAudio.Wave;

namespace TranscriptService.Audio;

/// <summary>Reads and validates the header of a WAV recording.</summary>
public static class AudioInspector
{
    /// <summary><c>KSDATAFORMAT_SUBTYPE_PCM</c> — linear PCM inside a <c>WAVE_FORMAT_EXTENSIBLE</c> header.</summary>
    private static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>Returns the format of the recording in <paramref name="wav"/>.</summary>
    /// <exception cref="UnsupportedAudioException">The stream is not a WAV file or uses an unsupported format.</exception>
    public static AudioFormatInfo Inspect(Stream wav)
    {
        using var reader = OpenReader(wav);
        return Describe(reader);
    }

    internal static WaveFileReader OpenReader(Stream wav)
    {
        try
        {
            return new WaveFileReader(wav);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or EndOfStreamException)
        {
            throw new UnsupportedAudioException($"The file is not a valid WAV recording: {ex.Message}", ex);
        }
    }

    internal static AudioFormatInfo Describe(WaveFileReader reader)
    {
        var format = reader.WaveFormat;
        var encoding = ToEncoding(format);

        if (format.Channels is not (1 or 2))
        {
            throw new UnsupportedAudioException($"Expected a mono or stereo recording; got {format.Channels} channels.");
        }

        if (reader.Length == 0)
        {
            throw new UnsupportedAudioException("The recording is empty.");
        }

        return new AudioFormatInfo(encoding, format.SampleRate, format.Channels, reader.TotalTime);
    }

    /// <summary>
    /// The sub-format GUID of a <c>WAVE_FORMAT_EXTENSIBLE</c> header. When reading files NAudio keeps
    /// the extension as raw bytes: valid bits (2), channel mask (4), then the sub-format (16).
    /// </summary>
    private static Guid? SubFormatOf(WaveFormat format) => format switch
    {
        WaveFormatExtensible extensible => extensible.SubFormat,
        WaveFormatExtraData { ExtraData.Length: >= 22 } extra => new Guid(extra.ExtraData.AsSpan(6, 16)),
        _ => null,
    };

    private static AudioEncoding ToEncoding(WaveFormat format) => (format.Encoding, format.BitsPerSample) switch
    {
        (WaveFormatEncoding.Pcm, 16) => AudioEncoding.Pcm16,
        (WaveFormatEncoding.Extensible, 16) when SubFormatOf(format) == PcmSubFormat => AudioEncoding.Pcm16,
        (WaveFormatEncoding.MuLaw, 8) => AudioEncoding.MuLaw,
        (WaveFormatEncoding.ALaw, 8) => AudioEncoding.ALaw,
        var (encoding, bits) => throw new UnsupportedAudioException(
            $"Expected PCM 16-bit, G.711 μ-law or G.711 A-law; got {encoding} {bits}-bit."),
    };
}
