using NAudio.Wave;

namespace TranscriptService.Audio.Tests;

public sealed class AudioPreprocessorTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task Load_8kHzMono_Returns16kHzWithDoubleSampleCount()
    {
        var source = WavFixtures.Sine(440, 8000, TimeSpan.FromSeconds(1));

        var audio = await LoadAsync(WavFixtures.Pcm16(source, 8000));

        audio.SampleRate.Should().Be(AudioPreprocessor.TargetSampleRate);
        audio.Samples.Length.Should().BeCloseTo(source.Length * 2, 1);
    }

    [Fact]
    public async Task Load_Stereo_AveragesChannels()
    {
        var audio = await LoadAsync(WavFixtures.Stereo(left: 0.5f, right: -0.5f, frames: 16000, sampleRate: 16000));

        audio.Samples.Should().OnlyContain(sample => Math.Abs(sample) < 0.001f);
    }

    [Fact]
    public async Task Load_Resample_PreservesTone()
    {
        var audio = await LoadAsync(WavFixtures.Pcm16(WavFixtures.Sine(1000, 8000, TimeSpan.FromSeconds(1)), 8000));

        // Skip the resampler's warm-up so the measurement covers the steady state only.
        var steady = audio.Samples.AsSpan(1600, 12800);
        var at1kHz = Magnitude(steady, 1000, audio.SampleRate);
        var at3kHz = Magnitude(steady, 3000, audio.SampleRate);

        at1kHz.Should().BeApproximately(0.5, 0.05, "the tone keeps its amplitude");
        at3kHz.Should().BeLessThan(0.01, "resampling must not add spectral images");
    }

    [Fact]
    public async Task Load_SampleAt8kHzMuLaw_DurationMatchesSource()
    {
        using var source = new WaveFileReader(WavFixtures.SamplePath);

        var audio = await LoadAsync(WavFixtures.SampleAt8kHzMuLaw());

        audio.Duration.Should().BeCloseTo(source.TotalTime, TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task Load_UnsupportedFormat_Throws()
    {
        var path = Temp(WavFixtures.IeeeFloat(new float[800], 8000));

        await FluentActions.Awaiting(() => AudioPreprocessor.LoadAsync(path, CancellationToken.None))
            .Should().ThrowAsync<UnsupportedAudioException>();
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            File.Delete(path);
        }
    }

    private Task<AudioSamples> LoadAsync(byte[] wav) =>
        AudioPreprocessor.LoadAsync(Temp(wav), TestContext.Current.CancellationToken);

    private string Temp(byte[] wav)
    {
        var path = WavFixtures.WriteTemp(wav);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>Amplitude of a single frequency (Goertzel), normalized so a full-scale sine of amplitude A gives A.</summary>
    private static double Magnitude(ReadOnlySpan<float> samples, double frequency, int sampleRate)
    {
        var coefficient = 2 * Math.Cos(2 * Math.PI * frequency / sampleRate);
        double previous = 0, beforePrevious = 0;
        foreach (var sample in samples)
        {
            var current = sample + (coefficient * previous) - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }

        var power = (previous * previous) + (beforePrevious * beforePrevious) - (coefficient * previous * beforePrevious);
        return 2 * Math.Sqrt(power) / samples.Length;
    }
}
