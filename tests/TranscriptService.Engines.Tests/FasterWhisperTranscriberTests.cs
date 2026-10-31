using Microsoft.Extensions.Logging.Abstractions;
using TranscriptService.Worker.FasterWhisper;

namespace TranscriptService.Engines.Tests;

public sealed class FasterWhisperTranscriberTests
{
    private static readonly string ModelDirectory = Environment.GetEnvironmentVariable("FASTERWHISPER_MODEL") ?? "fasterwhisper/large-v3-turbo";

    [Fact]
    public async Task Transcribe_TelephoneRecording_ReturnsPolishTextWithTimedSegments()
    {
        await using var transcriber = Create(LoadableModelOrSkip());
        await transcriber.InitializeAsync(TestContext.Current.CancellationToken);

        await TelephoneSample.AssertTranscribesAsync(transcriber, "utrudnienia", "przepraszamy");
    }

    [Fact]
    public async Task SupportedLanguages_ComeFromTheModel()
    {
        await using var transcriber = Create(LoadableModelOrSkip());
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

    private static string LoadableModelOrSkip()
    {
        // Measured with FasterWhisper.NET 1.0.8: its Windows CPU build returns no segments at all,
        // while the Linux builds (the worker's only target) transcribe correctly.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "FasterWhisper.NET 1.0.8 returns no output on Windows; run engine tests on Linux.");
        return ModelRequirement.PathOrSkip(ModelDirectory);
    }

    private static FasterWhisperTranscriber Create(string modelDirectory) => new(
        Microsoft.Extensions.Options.Options.Create(new FasterWhisperOptions { ModelPath = modelDirectory }),
        NullLogger<FasterWhisperTranscriber>.Instance);
}
