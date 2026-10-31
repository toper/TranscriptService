using System.Buffers.Binary;
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
/// <remarks>
/// Decoding is done here rather than with NAudio's format conversion streams because G.711
/// conversion lives in the Windows-only NAudio packages; NAudio.Core supplies the codec tables.
/// The file is streamed in chunks and decoded straight into one mono buffer, so a 30-minute
/// call never exists in memory as raw bytes and interleaved floats at the same time.
/// </remarks>
public static class AudioPreprocessor
{
    public const int TargetSampleRate = 16_000;

    private const int ChunkFrames = 16_384;
    private const float Pcm16Scale = 1f / 32768f;

    private static readonly float[] MuLawTable = BuildTable(MuLawDecoder.MuLawToLinearSample);
    private static readonly float[] ALawTable = BuildTable(ALawDecoder.ALawToLinearSample);

    /// <summary>Loads the WAV file at <paramref name="path"/> as 16 kHz mono samples.</summary>
    /// <remarks>
    /// Only the complete frames actually present are used: recordings cut short by a crashed
    /// recorder declare more data than the file holds, and may end in the middle of a frame.
    /// </remarks>
    /// <exception cref="UnsupportedAudioException">The file is not a supported WAV recording.</exception>
    public static async Task<AudioSamples> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (file.ConfigureAwait(false))
        {
            // The reader parses the header and leaves the stream at the start of the data chunk.
            using var reader = AudioInspector.OpenReader(file);
            var format = AudioInspector.Describe(reader);
            var blockAlign = reader.WaveFormat.BlockAlign;

            var available = Math.Min(reader.Length, file.Length - file.Position);
            var mono = new float[available / blockAlign];
            var chunk = new byte[ChunkFrames * blockAlign];
            var frames = 0;
            while (frames < mono.Length)
            {
                var wanted = (int)Math.Min(chunk.Length, (long)(mono.Length - frames) * blockAlign);
                var read = await file.ReadAtLeastAsync(chunk.AsMemory(0, wanted), wanted, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                var chunkFrames = read / blockAlign;
                DecodeToMono(chunk.AsSpan(0, chunkFrames * blockAlign), format, mono.AsSpan(frames, chunkFrames));
                frames += chunkFrames;
                if (read < wanted)
                {
                    break;
                }
            }

            if (frames == 0)
            {
                throw new UnsupportedAudioException("The recording is empty.");
            }

            return new AudioSamples(Resample(mono.AsMemory(0, frames), format.SampleRate), TargetSampleRate);
        }
    }

    private static void DecodeToMono(ReadOnlySpan<byte> data, AudioFormatInfo format, Span<float> mono)
    {
        var stereo = format.Channels == 2;
        if (format.Encoding == AudioEncoding.Pcm16)
        {
            for (var i = 0; i < mono.Length; i++)
            {
                var offset = i * format.Channels * 2;
                var sample = BinaryPrimitives.ReadInt16LittleEndian(data[offset..]) * Pcm16Scale;
                mono[i] = stereo ? (sample + (BinaryPrimitives.ReadInt16LittleEndian(data[(offset + 2)..]) * Pcm16Scale)) / 2f : sample;
            }

            return;
        }

        var table = format.Encoding == AudioEncoding.MuLaw ? MuLawTable : ALawTable;
        for (var i = 0; i < mono.Length; i++)
        {
            mono[i] = stereo ? (table[data[2 * i]] + table[data[(2 * i) + 1]]) / 2f : table[data[i]];
        }
    }

    private static float[] Resample(ReadOnlyMemory<float> mono, int sampleRate)
    {
        if (sampleRate == TargetSampleRate)
        {
            return MemoryMarshal.TryGetArray(mono, out var segment) && segment.Count == segment.Array!.Length
                ? segment.Array
                : mono.ToArray();
        }

        var resampler = new WdlResamplingSampleProvider(new MemorySampleProvider(mono, sampleRate), TargetSampleRate);
        var output = new float[(int)Math.Ceiling((double)mono.Length * TargetSampleRate / sampleRate)];
        var written = 0;
        int read;
        while (written < output.Length && (read = resampler.Read(output.AsSpan(written))) > 0)
        {
            written += read;
        }

        // The windowed-sinc filter can hold back a few samples at the very end; trim if it did.
        return written == output.Length ? output : output[..written];
    }

    private static float[] BuildTable(Func<byte, short> decode)
    {
        var table = new float[256];
        for (var value = 0; value < table.Length; value++)
        {
            table[value] = decode((byte)value) * Pcm16Scale;
        }

        return table;
    }

    private sealed class MemorySampleProvider(ReadOnlyMemory<float> samples, int sampleRate) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, samples.Length - _position);
            samples.Span.Slice(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
    }
}
