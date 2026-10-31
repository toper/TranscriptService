using Microsoft.Extensions.Logging.Abstractions;
using TranscriptService.Audio;
using TranscriptService.Audio.Tests;
using TranscriptService.Worker.WhisperNet;

namespace TranscriptService.Engines.Tests;

public sealed class WhisperNetTranscriberTests
{
    private static readonly string ModelFile = Environment.GetEnvironmentVariable("WHISPERNET_MODEL") ?? "whispernet/ggml-large-v3-turbo-q5_0.bin";

    [Fact]
    public async Task Transcribe_TelephoneRecording_ReturnsPolishTextWithTimedSegments()
    {
        await using var transcriber = Create(ModelRequirement.PathOrSkip(ModelFile));
        await transcriber.InitializeAsync(TestContext.Current.CancellationToken);
        var call = WavFixtures.WriteTemp(WavFixtures.SampleAt8kHzMuLaw());
        try
        {
            var audio = await AudioPreprocessor.LoadAsync(call, TestContext.Current.CancellationToken);

            var output = await transcriber.TranscribeAsync(audio, "pl", TestContext.Current.CancellationToken);

            // The sample is synthetic speech (eSpeak NG); these words survive 8 kHz μ-law reliably.
            output.FullText.Should().ContainEquivalentOf("Konarskiego").And.ContainEquivalentOf("przepraszamy");
            output.Segments.Should().NotBeEmpty().And.BeInAscendingOrder(segment => segment.Start);
            output.Segments.Should().OnlyContain(segment => segment.End > segment.Start && segment.End <= audio.Duration + TimeSpan.FromSeconds(1));
        }
        finally
        {
            File.Delete(call);
        }
    }

    [Fact]
    public async Task Transcribe_UnsupportedLanguage_IsRejectedWithReason()
    {
        await using var transcriber = Create(ModelRequirement.PathOrSkip(ModelFile));
        await transcriber.InitializeAsync(TestContext.Current.CancellationToken);
        var silence = new AudioSamples(new float[16_000], AudioPreprocessor.TargetSampleRate);

        await FluentActions.Awaiting(() => transcriber.TranscribeAsync(silence, "xx", TestContext.Current.CancellationToken))
            .Should().ThrowAsync<TranscriptService.Worker.Core.TranscriptionRejectedException>().WithMessage("*'xx'*");
    }

    [Theory]
    [InlineData("[BLANK_AUDIO]")]
    [InlineData("(muzyka)")]
    [InlineData("[Muzyka w tle]")]
    [InlineData("")]
    public void IsSpeech_NonSpeechMarkers_AreDropped(string text) =>
        WhisperNetTranscriber.IsSpeech(text).Should().BeFalse();

    [Theory]
    [InlineData("Dzień dobry.")]
    [InlineData("Paleta (euro) kosztuje 100 zł.")]
    public void IsSpeech_Sentences_AreKept(string text) =>
        WhisperNetTranscriber.IsSpeech(text).Should().BeTrue();

    [Fact]
    public async Task Initialize_MissingModel_Throws()
    {
        await using var transcriber = Create(Path.Combine(Path.GetTempPath(), "missing-model.bin"));

        await FluentActions.Awaiting(() => transcriber.InitializeAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    private static WhisperNetTranscriber Create(string modelPath) => new(
        Microsoft.Extensions.Options.Options.Create(new WhisperNetOptions { ModelPath = modelPath }),
        NullLogger<WhisperNetTranscriber>.Instance);
}
