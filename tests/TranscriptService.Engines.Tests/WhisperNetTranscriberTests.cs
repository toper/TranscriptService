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
            output.Segments.Should().OnlyContain(segment => segment.End > segment.Start && segment.End <= audio.Duration);
        }
        finally
        {
            File.Delete(call);
        }
    }

    [Fact]
    public async Task SupportedLanguages_IncludePolishAndEnglish()
    {
        await using var transcriber = Create(ModelRequirement.PathOrSkip(ModelFile));
        await transcriber.InitializeAsync(TestContext.Current.CancellationToken);

        transcriber.SupportedLanguages.Should().Contain(["pl", "en"]).And.NotContain("xx");
    }

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
