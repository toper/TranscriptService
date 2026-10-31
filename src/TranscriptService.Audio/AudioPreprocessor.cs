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
    /// <remarks>
    /// Only the complete frames actually present are used: recordings cut short by a crashed
    /// recorder declare more data than the file holds, and may end in the middle of a frame.
    /// </remarks>
    /// <exception cref="UnsupportedAudioException">The file is not a supported WAV recording.</exception>
    public static async Task<AudioSamples> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16, FileOptions.Asynchronous);
        await using (file.ConfigureAwait(false))
        {
            // The reader parses the header and leaves the stream at the start of the data chunk.
            using var reader = AudioInspector.OpenReader(file);
            var format = AudioInspector.Describe(reader);

            var data = new byte[Math.Min(reader.Length, file.Length - file.Position)];
            var filled = await file.ReadAtLeastAsync(data, data.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            var completeFrames = filled - (filled % reader.WaveFormat.BlockAlign);
            if (completeFrames == 0)
            {
                throw new UnsupportedAudioException("The recording is empty.");
            }

            var mono = Downmix(Decode(data.AsSpan(0, completeFrames), format.Encoding), format.Channels);
            return new AudioSamples(Resample(mono, format.SampleRate), TargetSampleRate);
        }
    }

    private static float[] Decode(ReadOnlySpan<byte> data, AudioEncoding encoding)
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
        var decoded = new float[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            decoded[i] = decode(data[i]) * Scale;
        }

        return decoded;
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
