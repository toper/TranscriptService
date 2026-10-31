using System.Text.Json;
using TranscriptService.Worker.FasterWhisper;

namespace TranscriptService.Engines.Tests;

/// <summary>Language detection from CTranslate2 model files — no model weights needed.</summary>
public sealed class FasterWhisperLanguagesTests : IDisposable
{
    private readonly DirectoryInfo _model = Directory.CreateTempSubdirectory("ct2-model-");

    [Fact]
    public async Task Multilingual_LanguagesAreTheTokensListedInLangIds()
    {
        await WriteAsync("vocabulary.json", JsonSerializer.Serialize(new[] { "<|endoftext|>", "<|en|>", "<|pl|>", "<|yue|>", "<|transcribe|>" }));
        await WriteAsync("config.json", """{ "lang_ids": [1, 2, 3] }""");

        (await ReadAsync()).Should().BeEquivalentTo(["en", "pl", "yue"]);
    }

    [Fact]
    public async Task EnglishOnlyModel_SupportsOnlyEnglish_EvenWithAMultilingualVocabulary()
    {
        await WriteAsync("vocabulary.json", JsonSerializer.Serialize(new[] { "<|endoftext|>", "<|en|>", "<|pl|>" }));
        await WriteAsync("config.json", """{ "suppress_ids": [] }""");

        (await ReadAsync()).Should().BeEquivalentTo(["en"]);
    }

    [Fact]
    public async Task OlderConversion_WithVocabularyTxt_IsSupported()
    {
        await WriteAsync("vocabulary.txt", "<|endoftext|>\n<|en|>\n<|de|>\n");
        await WriteAsync("config.json", """{ "lang_ids": [1, 2] }""");

        (await ReadAsync()).Should().BeEquivalentTo(["en", "de"]);
    }

    public void Dispose() => _model.Delete(recursive: true);

    private Task WriteAsync(string name, string content) =>
        File.WriteAllTextAsync(Path.Combine(_model.FullName, name), content, TestContext.Current.CancellationToken);

    private Task<IReadOnlySet<string>> ReadAsync() =>
        FasterWhisperTranscriber.ReadLanguagesAsync(_model.FullName, TestContext.Current.CancellationToken);
}
