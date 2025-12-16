using FluentAssertions;
using TranscriptService.Models;
using TranscriptService.VoskAPI;
using TranscriptService.Whisper;

namespace TranscriptService.Tests.Integration;

/// <summary>
/// Testy integracyjne transkrypcji z użyciem rzeczywistych plików audio
///
/// UWAGA: Te testy są oznaczone jako Skip, ponieważ wymagają:
/// - Rzeczywistego pliku audio w TestData/Audio/sample.wav
/// - Modelu Vosk (dla testów VoskTranscriber)
/// - Klucza API OpenAI (dla testów WhisperTranscriber)
///
/// Aby uruchomić te testy:
/// 1. Umieść plik audio w TranscriptService.Tests/TestData/Audio/sample.wav
/// 2. Usuń atrybut [Fact(Skip = "...")] i zastąp go [Fact]
/// 3. Skonfiguruj wymagane zmienne środowiskowe lub parametry
/// </summary>
public class TranscriptionIntegrationTests
{
    [Fact]
    public void TestHelper_ShouldLocateTestAudioFile()
    {
        // Arrange & Act
        var exists = TestHelper.TestFileExists("Audio/sample.wav");

        // Assert
        exists.Should().BeTrue("plik testowy powinien istnieć w TestData/Audio/sample.wav");
    }

    [Fact(Skip = "Requires sample audio file and Vosk model")]
    public async Task VoskTranscriber_ShouldTranscribeSampleAudio()
    {
        // Arrange
        var modelPath = Environment.GetEnvironmentVariable("VOSK_MODEL_PATH")
            ?? @"C:\vosk-models\vosk-model-small-pl-0.22";

        var transcriber = new VoskTranscriber(modelPath);
        var audioFilePath = TestHelper.GetTestFilePath("Audio/sample.wav");

        // Act
        var result = await transcriber.TranscribeAsync(audioFilePath);

        // Assert
        result.Should().NotBeNull();
        result.FullText.Should().NotBeNullOrWhiteSpace();
        result.Transcription.Should().NotBeEmpty();

        // Opcjonalnie: sprawdź czy tekst zawiera oczekiwane słowa
        // result.FullText.Should().Contain("oczekiwane słowo");
    }

    [Fact(Skip = "Requires sample audio file and OpenAI API key")]
    public async Task WhisperTranscriber_ShouldTranscribeSampleAudio()
    {
        // Arrange
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("Brak zmiennej środowiskowej OPENAI_API_KEY");

        var transcriber = new WhisperTranscriber(apiKey);
        var audioFilePath = TestHelper.GetTestFilePath("Audio/sample.wav");

        // Act
        var result = await transcriber.TranscribeAsync(audioFilePath);

        // Assert
        result.Should().NotBeNull();
        result.FullText.Should().NotBeNullOrWhiteSpace();
        result.Transcription.Should().NotBeEmpty();

        // Whisper zwraca timestampy
        result.Transcription.Should().AllSatisfy(word =>
        {
            word.Word.Should().NotBeNullOrWhiteSpace();
            word.StartTime.Should().BeGreaterOrEqualTo(0);
            word.EndTime.Should().BeGreaterThan(word.StartTime);
        });
    }

    [Fact(Skip = "Requires sample audio file and both Vosk model and OpenAI API key")]
    public async Task BothTranscribers_ShouldProduceSimilarResults()
    {
        // Arrange
        var modelPath = Environment.GetEnvironmentVariable("VOSK_MODEL_PATH")
            ?? @"C:\vosk-models\vosk-model-small-pl-0.22";
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("Brak zmiennej środowiskowej OPENAI_API_KEY");

        var voskTranscriber = new VoskTranscriber(modelPath);
        var whisperTranscriber = new WhisperTranscriber(apiKey);
        var audioFilePath = TestHelper.GetTestFilePath("Audio/sample.wav");

        // Act
        var voskResult = await voskTranscriber.TranscribeAsync(audioFilePath);
        var whisperResult = await whisperTranscriber.TranscribeAsync(audioFilePath);

        // Assert
        voskResult.Should().NotBeNull();
        whisperResult.Should().NotBeNull();

        // Oba powinny zwrócić jakiś tekst
        voskResult.FullText.Should().NotBeNullOrWhiteSpace();
        whisperResult.FullText.Should().NotBeNullOrWhiteSpace();

        // Porównanie liczby słów (z tolerancją)
        var voskWordCount = voskResult.Transcription.Count;
        var whisperWordCount = whisperResult.Transcription.Count;

        var difference = Math.Abs(voskWordCount - whisperWordCount);
        var averageCount = (voskWordCount + whisperWordCount) / 2.0;
        var percentageDifference = (difference / averageCount) * 100;

        percentageDifference.Should().BeLessThan(30,
            "różnica w liczbie słów między Vosk a Whisper nie powinna przekraczać 30%");
    }
}
