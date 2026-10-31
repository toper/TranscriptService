namespace TranscriptService.Audio.Tests;

public sealed class AudioInspectorTests
{
    private static readonly float[] OneSecondAt8kHz = WavFixtures.Sine(440, 8000, TimeSpan.FromSeconds(1));

    [Fact]
    public void Inspect_Pcm16Mono8kHz_ReturnsFormat()
    {
        var format = Inspect(WavFixtures.Pcm16(OneSecondAt8kHz, 8000));

        format.Encoding.Should().Be(AudioEncoding.Pcm16);
        format.SampleRate.Should().Be(8000);
        format.Channels.Should().Be(1);
        format.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Inspect_MuLaw_IsRecognized() =>
        Inspect(WavFixtures.MuLaw(OneSecondAt8kHz, 8000)).Encoding.Should().Be(AudioEncoding.MuLaw);

    [Fact]
    public void Inspect_ALaw_IsRecognized() =>
        Inspect(WavFixtures.ALaw(OneSecondAt8kHz, 8000)).Encoding.Should().Be(AudioEncoding.ALaw);

    [Fact]
    public void Inspect_AcceptsExtensibleHeaderAndExtraChunks()
    {
        var format = Inspect(WavFixtures.WithListChunk(WavFixtures.Extensible(OneSecondAt8kHz, 8000)));

        format.Encoding.Should().Be(AudioEncoding.Pcm16);
        format.Duration.Should().BeCloseTo(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void Inspect_24BitPcm_IsRejectedWithDetectedFormat() =>
        FluentActions.Invoking(() => Inspect(WavFixtures.Pcm24(OneSecondAt8kHz, 8000)))
            .Should().Throw<UnsupportedAudioException>().WithMessage("*24-bit*");

    [Fact]
    public void Inspect_FloatPcm_IsRejectedWithDetectedFormat() =>
        FluentActions.Invoking(() => Inspect(WavFixtures.IeeeFloat(OneSecondAt8kHz, 8000)))
            .Should().Throw<UnsupportedAudioException>().WithMessage("*IeeeFloat*");

    [Fact]
    public void Inspect_ThreeChannels_IsRejected() =>
        FluentActions.Invoking(() => Inspect(WavFixtures.Pcm16(new float[300], 8000, channels: 3)))
            .Should().Throw<UnsupportedAudioException>().WithMessage("*3 channels*");

    [Fact]
    public void Inspect_NotAWav_IsRejected() =>
        FluentActions.Invoking(() => Inspect("this is not audio"u8.ToArray()))
            .Should().Throw<UnsupportedAudioException>().WithMessage("*not a valid WAV*");

    [Fact]
    public void Inspect_EmptyRecording_IsRejected() =>
        FluentActions.Invoking(() => Inspect(WavFixtures.Pcm16([], 8000)))
            .Should().Throw<UnsupportedAudioException>().WithMessage("*empty*");

    private static AudioFormatInfo Inspect(byte[] wav)
    {
        using var stream = new MemoryStream(wav);
        return AudioInspector.Inspect(stream);
    }
}
