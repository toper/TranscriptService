using System.Runtime.InteropServices;
using NAudio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TranscriptService.Audio;

/// <summary>
/// Turns a stored recording into what Whisper and Vosk expect: 16 kHz mono float samples.
/// Telephone audio (8 kHz, G.711, stereo agent/customer channels) is decoded, downmixed by
/// averaging and resampled with a windowed-sinc resampler, which — unlike linear
/// interpolation — does not add spectral images into the band the models listen to.
/// </summary>
public static class AudioPreprocessor
{
    public const int TargetSampleRate = 16_000;

    /// <summary>Loads the WAV file at <paramref name="path"/> as 16 kHz mono samples.</summary>
    /// <exception cref="UnsupportedAudioException">The file is not a supported WAV recording.</exception>
    public static async Task<AudioSamples> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        using var reader = AudioInspector.OpenReader(new MemoryStream(bytes, writable: false));
        var format = AudioInspector.Describe(reader);

        var data = new byte[reader.Length];
        reader.ReadExactly(data);

        var mono = Downmix(Decode(data, format.Encoding), format.Channels);
        return new AudioSamples(Resample(mono, format.SampleRate), TargetSampleRate);
    }

    private static float[] Decode(byte[] data, AudioEncoding encoding)
    {
        const float Scale = 1f / 32768f;
        if (encoding == AudioEncoding.Pcm16)
        {
            var pcm = MemoryMarshal.Cast<byte, short>(data);
            var samples = new float[pcm.Length];
            for (var i = 0; i < pcm.Length; i++)
            {
                samples[i] = pcm[i] * Scale;
            }

            return samples;
        }

        Func<byte, short> decode = encoding == AudioEncoding.MuLaw
            ? MuLawDecoder.MuLawToLinearSample
            : ALawDecoder.ALawToLinearSample;
        return Array.ConvertAll(data, b => decode(b) * Scale);
    }

    private static float[] Downmix(float[] interleaved, int channels)
    {
        if (channels == 1)
        {
            return interleaved;
        }

        var mono = new float[interleaved.Length / 2];
        for (var i = 0; i < mono.Length; i++)
        {
            mono[i] = (interleaved[2 * i] + interleaved[(2 * i) + 1]) / 2f;
        }

        return mono;
    }

    private static float[] Resample(float[] mono, int sampleRate)
    {
        if (sampleRate == TargetSampleRate)
        {
            return mono;
        }

        var source = new ArraySampleProvider(mono, sampleRate);
        var resampler = new WdlResamplingSampleProvider(source, TargetSampleRate);
        var output = new float[(int)Math.Ceiling((double)mono.Length * TargetSampleRate / sampleRate) + 4096];
        var written = 0;
        int read;
        while ((read = resampler.Read(output.AsSpan(written, Math.Min(4096, output.Length - written)))) > 0)
        {
            written += read;
        }

        return output[..written];
    }

    private sealed class ArraySampleProvider(float[] samples, int sampleRate) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var available = Math.Min(buffer.Length, samples.Length - _position);
            samples.AsSpan(_position, available).CopyTo(buffer);
            _position += available;
            return available;
        }
    }
}
