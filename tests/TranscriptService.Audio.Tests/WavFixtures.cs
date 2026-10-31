using System.Buffers.Binary;
using System.Text;
using NAudio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TranscriptService.Audio.Tests;

/// <summary>Builds WAV recordings in memory so tests never depend on binary fixtures other than sample.wav.</summary>
internal static class WavFixtures
{
    public static string SamplePath => Path.Combine(AppContext.BaseDirectory, "TestData", "sample.wav");

    public static byte[] Pcm16(float[] samples, int sampleRate, int channels = 1) =>
        Write(new WaveFormat(sampleRate, 16, channels), samples, static (writer, sample) => writer.WriteSample(sample));

    public static byte[] Extensible(float[] samples, int sampleRate) =>
        Write(new WaveFormatExtensible(sampleRate, 16, 1), samples, static (writer, sample) => writer.WriteSample(sample));

    public static byte[] MuLaw(float[] samples, int sampleRate) =>
        WriteEncoded(WaveFormat.CreateMuLawFormat(sampleRate, 1), samples, MuLawEncoder.LinearToMuLawSample);

    public static byte[] ALaw(float[] samples, int sampleRate) =>
        WriteEncoded(WaveFormat.CreateALawFormat(sampleRate, 1), samples, ALawEncoder.LinearToALawSample);

    public static byte[] Pcm24(float[] samples, int sampleRate) =>
        Write(new WaveFormat(sampleRate, 24, 1), samples, static (writer, sample) => writer.WriteSample(sample));

    public static byte[] IeeeFloat(float[] samples, int sampleRate) =>
        Write(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1), samples, static (writer, sample) => writer.WriteSample(sample));

    /// <summary>Stereo PCM16 where every frame is (left, right).</summary>
    public static byte[] Stereo(float left, float right, int frames, int sampleRate)
    {
        var interleaved = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            interleaved[2 * i] = left;
            interleaved[(2 * i) + 1] = right;
        }

        return Pcm16(interleaved, sampleRate, channels: 2);
    }

    /// <summary>Inserts a <c>LIST</c> chunk between <c>fmt </c> and <c>data</c>, as many recorders do.</summary>
    public static byte[] WithListChunk(byte[] wav)
    {
        var dataOffset = IndexOf(wav, "data"u8);
        byte[] listPayload = [.. "INFOISFT"u8, 6, 0, 0, 0, .. "test\0\0"u8];
        var chunk = new byte[8 + listPayload.Length];
        "LIST"u8.CopyTo(chunk);
        BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), listPayload.Length);
        listPayload.CopyTo(chunk, 8);

        byte[] result = [.. wav.AsSpan(0, dataOffset), .. chunk, .. wav.AsSpan(dataOffset)];
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), result.Length - 8);
        return result;
    }

    public static float[] Sine(double frequency, int sampleRate, TimeSpan duration, float amplitude = 0.5f)
    {
        var samples = new float[(int)(sampleRate * duration.TotalSeconds)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = amplitude * (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
        }

        return samples;
    }

    /// <summary>The repository sample resampled to 8 kHz and encoded as G.711 μ-law — a typical call recording.</summary>
    public static byte[] SampleAt8kHzMuLaw()
    {
        using var reader = new WaveFileReader(SamplePath);
        var resampled = new WdlResamplingSampleProvider(reader.ToSampleProvider().ToMono(), 8000);
        var samples = new List<float>();
        var buffer = new float[4096];
        int read;
        while ((read = resampled.Read(buffer)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }

        return MuLaw([.. samples], 8000);
    }

    public static string WriteTemp(byte[] wav)
    {
        var path = Path.Combine(Path.GetTempPath(), $"audio-test-{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(path, wav);
        return path;
    }

    private static byte[] Write(WaveFormat format, float[] samples, Action<WaveFileWriter, float> writeSample)
    {
        using var output = new MemoryStream();
        using var writer = new WaveFileWriter(output, format);
        foreach (var sample in samples)
        {
            writeSample(writer, sample);
        }

        writer.Flush(); // writes the final RIFF and data chunk sizes
        return output.ToArray();
    }

    private static byte[] WriteEncoded(WaveFormat format, float[] samples, Func<short, byte> encode)
    {
        using var output = new MemoryStream();
        using var writer = new WaveFileWriter(output, format);
        var encoded = samples.Select(s => encode((short)(Math.Clamp(s, -1f, 1f) * short.MaxValue))).ToArray();
        writer.Write(encoded, 0, encoded.Length);

        writer.Flush(); // writes the final RIFF and data chunk sizes
        return output.ToArray();
    }

    private static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        var index = haystack.AsSpan().IndexOf(needle);
        return index >= 0 ? index : throw new InvalidOperationException($"Chunk '{Encoding.ASCII.GetString(needle)}' not found.");
    }
}
