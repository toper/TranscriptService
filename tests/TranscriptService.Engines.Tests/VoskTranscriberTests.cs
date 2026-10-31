using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging.Abstractions;
using TranscriptService.Worker.Core;
using TranscriptService.Worker.Vosk;

namespace TranscriptService.Engines.Tests;

public sealed class VoskTranscriberTests
{
    private static readonly string ModelDirectory = Environment.GetEnvironmentVariable("VOSK_MODEL") ?? "vosk/small-pl";

    [Fact]
    public async Task Transcribe_TelephoneRecording_ReturnsTimedWords()
    {
        await using var transcriber = Create(ModelRequirement.PathOrSkip(ModelDirectory));
        await transcriber.InitializeAsync(TestContext.Current.CancellationToken);

        await TelephoneSample.AssertTranscribesAsync(transcriber, "przepraszamy");
    }

    [Fact]
    public async Task SupportedLanguages_IsTheModelLanguage()
    {
        await using var transcriber = Create(ModelRequirement.PathOrSkip(ModelDirectory));
        await transcriber.InitializeAsync(TestContext.Current.CancellationToken);

        transcriber.SupportedLanguages.Should().BeEquivalentTo(["pl"]);
    }

    [Fact]
    public async Task Initialize_MissingModel_Throws()
    {
        await using var transcriber = Create(Path.Combine(Path.GetTempPath(), $"missing-vosk-{Guid.NewGuid():N}"));

        await FluentActions.Awaiting(() => transcriber.InitializeAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public void Options_CudaDevice_FailValidation()
    {
        var options = new VoskOptions { ModelPath = "/models/vosk/small-pl", Device = ComputeDevice.Cuda };
        var errors = new List<ValidationResult>();

        Validator.TryValidateObject(options, new ValidationContext(options), errors, validateAllProperties: true).Should().BeFalse();
        errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("CPU");
    }

    private static VoskTranscriber Create(string modelDirectory) => new(
        Microsoft.Extensions.Options.Options.Create(new VoskOptions { ModelPath = modelDirectory }),
        NullLogger<VoskTranscriber>.Instance);
}
