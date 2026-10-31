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
    public async Task Initialize_DirectoryWithoutAModel_FailsAtStartup()
    {
        var empty = Directory.CreateTempSubdirectory("not-a-vosk-model-");
        try
        {
            await using var transcriber = Create(empty.FullName);

            await FluentActions.Awaiting(() => transcriber.InitializeAsync(TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>().WithMessage("*could not load*");
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Initialize_MissingModel_Throws()
    {
        await using var transcriber = Create(Path.Combine(Path.GetTempPath(), $"missing-vosk-{Guid.NewGuid():N}"));

        await FluentActions.Awaiting(() => transcriber.InitializeAsync(TestContext.Current.CancellationToken))
            .Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public void Options_WithoutModelLanguage_FailValidation()
    {
        Validate(new VoskOptions { ModelPath = "/models/vosk/small-pl" })
            .Should().ContainSingle().Which.MemberNames.Should().Contain(nameof(VoskOptions.ModelLanguage));
    }

    [Fact]
    public void Options_CudaDevice_FailValidation()
    {
        Validate(new VoskOptions { ModelPath = "/models/vosk/small-pl", ModelLanguage = "pl", Device = ComputeDevice.Cuda })
            .Should().ContainSingle().Which.ErrorMessage.Should().Contain("CPU");
    }

    private static List<ValidationResult> Validate(VoskOptions options)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), errors, validateAllProperties: true);
        return errors;
    }

    private static VoskTranscriber Create(string modelDirectory) => new(
        Microsoft.Extensions.Options.Options.Create(new VoskOptions { ModelPath = modelDirectory, ModelLanguage = "pl" }),
        NullLogger<VoskTranscriber>.Instance);
}
