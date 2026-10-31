using Microsoft.Extensions.Logging.Abstractions;
using TranscriptService.Audio;
using TranscriptService.Audio.Tests;
using TranscriptService.Worker.FasterWhisper;

namespace TranscriptService.Engines.Tests;

public sealed class FasterWhisperTranscriberTests
{
    private static readonly string ModelDirectory = Environment.GetEnvironmentVariable("FASTERWHISPER_MODEL") ?? "fasterwhisper/large-v3-turbo";

    [Fact]
    public async Task Transcribe_TelephoneRecording_ReturnsPolishTextWithTimedSegments()
    {
        await using var transcriber = Create(ModelRequirement.PathOrSkip(ModelDirectory));
        await transcriber.InitializeAsync(TestContext.Current.CancellationToken);
        var call = WavFixtures.WriteTemp(WavFixtures.SampleAt8kHzMuLaw());
        try
        {
            var audio = await AudioPreprocessor.LoadAsync(call, TestContext.Current.CancellationToken);

            var output = await transcriber.TranscribeAsync(audio, "pl", TestContext.Current.CancellationToken);

            // The sample is synthetic speech (eSpeak NG); these words survive 8 kHz μ-law reliably.
            output.FullText.Should().ContainEquivalentOf("utrudnienia").And.ContainEquivalentOf("przepraszamy");
            output.Segments.Should().NotBeEmpty().And.BeInAscendingOrder(segment => segment.Start);
            output.Segments.Should().OnlyContain(segment => segment.End > segment.Start && segment.End <= audio.Duration);
        }
        finally
        {
            File.Delete(call);
        }
    }

    [Fact]
    public async Task SupportedLanguages_ComeFromTheModelVocabulary()
    {
        await using var transcriber = Create(ModelRequirement.PathOrSkip(ModelDirectory));
        await transcriber.InitializeAsync(TestContext.Current.CancellationToken);

        transcriber.SupportedLanguages.Should().HaveCount(100).And.Contain(["pl", "en", "yue"]).And.NotContain("xx");
    }

    [Fact]
    public async Task Initialize_MissingModel_Throws()
    {
        await using var transcriber = Create(Path.Combine(Path.GetTempPath(), $"missing-model-{Guid.NewGuid():N}"));

        await FluentActions.Awaiting(() => transcriber.InitializeAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<DirectoryNotFoundException>();
    }

    private static FasterWhisperTranscriber Create(string modelDirectory)
    {
        // Measured with FasterWhisper.NET 1.0.8: its Windows CPU build returns no segments at all,
        // while the Linux builds (the worker's only target) transcribe correctly.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "FasterWhisper.NET 1.0.8 returns no output on Windows; run engine tests on Linux.");
        return new FasterWhisperTranscriber(
            Microsoft.Extensions.Options.Options.Create(new FasterWhisperOptions { ModelPath = modelDirectory }),
            NullLogger<FasterWhisperTranscriber>.Instance);
    }
}
