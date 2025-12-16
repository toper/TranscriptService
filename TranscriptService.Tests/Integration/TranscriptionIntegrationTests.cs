using FluentAssertions;
using TranscriptService.Models;
using TranscriptService.VoskAPI;
using TranscriptService.Whisper;
using Whisper.net.Wave;

namespace TranscriptService.Tests.Integration;

/// <summary>
/// Testy integracyjne transkrypcji z użyciem rzeczywistych plików audio
///
/// WYMAGANIA:
/// 1. Plik audio sample.wav w TestData/Audio/ (musi być 16kHz, mono, WAV)
///    - Konwersja: ffmpeg -i input.wav -ar 16000 -ac 1 TestData/Audio/sample.wav
///
/// 2. Model Whisper GGML (dla testów WhisperTranscriber):
///    - Pobierz z: https://huggingface.co/ggerganov/whisper.cpp/tree/main
///    - Zalecane: ggml-base.bin (~142 MB)
///    - Ustaw zmienną środowiskową WHISPER_MODEL_PATH lub umieść w D:\Models\ggml-base.bin
///
/// 3. Model Vosk (dla testów VoskTranscriber):
///    - Ustaw zmienną środowiskową VOSK_MODEL_PATH lub umieść w domyślnej lokalizacji
///
/// UWAGA: Test WhisperTranscriber_ShouldTranscribeSampleAudio będzie automatycznie pominięty
/// z jasnym komunikatem, jeśli model lub plik audio nie spełnia wymagań.
/// </summary>
public class TranscriptionIntegrationTests
{
    public TranscriptionIntegrationTests()
    {
        Environment.SetEnvironmentVariable("WHISPER_MODEL_PATH", "d:\\GIT\\TranscriptService\\TranscriptService.Whisper\\WhisperModel\\ggml-large-v3.bin", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("VOSK_MODEL_PATH", "d:\\GIT\\TranscriptService\\TranscriptService.VoskAPI\\VoskModel\\vosk-model-small-pl-0.22", EnvironmentVariableTarget.Process);
    }

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

    [Fact]
    public async Task WhisperTranscriber_ShouldTranscribeSampleAudio()
    {
        // Arrange
        var modelPath = Environment.GetEnvironmentVariable("WHISPER_MODEL_PATH")
            ?? @"D:\Models\ggml-base.bin";

        // Skip test if model doesn't exist
        if (!File.Exists(modelPath))
        {
            Assert.Fail($"SKIP: Model Whisper nie został znaleziony w: {modelPath}. " +
                       "Pobierz model z https://huggingface.co/ggerganov/whisper.cpp/tree/main " +
                       "lub ustaw zmienną środowiskową WHISPER_MODEL_PATH");
        }

        var transcriber = new WhisperTranscriber(modelPath);
        var audioFilePath = TestHelper.GetTestFilePath("Audio/sample.wav");

        TranscriptionResult result;
        try
        {
            // Act
            result = await transcriber.TranscribeAsync(audioFilePath);
        }
        catch (NotSupportedWaveException ex) when (ex.Message.Contains("16KHz"))
        {
            // Clean up before skipping
            transcriber.Dispose();

            Assert.Fail("SKIP: Plik audio sample.wav musi mieć częstotliwość próbkowania 16kHz. " +
                       "Użyj narzędzia do konwersji (np. ffmpeg): " +
                       "ffmpeg -i sample.wav -ar 16000 sample_16k.wav");
            return; 
        }

        // Assert
        result.Should().NotBeNull();
        //result.FullText.Should().NotBeNullOrWhiteSpace();
        result.FullText.Should().Contain("Kościuszki");
        result.Transcription.Should().NotBeEmpty();

        // Whisper zwraca timestampy dla segmentów
        result.Transcription.Should().AllSatisfy(segment =>
        {
            segment.Word.Should().NotBeNullOrWhiteSpace();
            segment.StartTime.Should().BeGreaterOrEqualTo(0);
            segment.EndTime.Should().BeGreaterThan(segment.StartTime);
        });

        // Clean up
        transcriber.Dispose();
    }

    [Fact(Skip = "Requires sample audio file and both Vosk and Whisper models")]
    public async Task BothTranscribers_ShouldProduceSimilarResults()
    {
        // Arrange
        var voskModelPath = Environment.GetEnvironmentVariable("VOSK_MODEL_PATH")
            ?? @"C:\vosk-models\vosk-model-small-pl-0.22";
        var whisperModelPath = Environment.GetEnvironmentVariable("WHISPER_MODEL_PATH")
            ?? @"D:\Models\ggml-base.bin";

        var voskTranscriber = new VoskTranscriber(voskModelPath);
        var whisperTranscriber = new WhisperTranscriber(whisperModelPath);
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

        // Porównanie liczby elementów transkrypcji (z tolerancją)
        // Uwaga: Vosk zwraca słowa, Whisper zwraca segmenty
        var voskWordCount = voskResult.Transcription.Count;
        var whisperSegmentCount = whisperResult.Transcription.Count;

        var difference = Math.Abs(voskWordCount - whisperSegmentCount);
        var averageCount = (voskWordCount + whisperSegmentCount) / 2.0;
        var percentageDifference = (difference / averageCount) * 100;

        percentageDifference.Should().BeLessThan(50,
            "różnica w liczbie elementów między Vosk (słowa) a Whisper (segmenty) nie powinna być zbyt duża");

        // Clean up
        whisperTranscriber.Dispose();
    }
}
