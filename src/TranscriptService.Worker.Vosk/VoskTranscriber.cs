using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TranscriptService.Audio;
using TranscriptService.Contracts;
using TranscriptService.Worker.Core;
using Vosk;

namespace TranscriptService.Worker.Vosk;

/// <summary>
/// Vosk (Kaldi) engine: a lightweight CPU recognizer with word-level timestamps. The model is loaded
/// once and shared; each job gets its own recognizer, as recognizers keep per-stream state.
/// </summary>
public sealed partial class VoskTranscriber(
    IOptions<VoskOptions> options,
    ILogger<VoskTranscriber> logger) : ITranscriber
{
    /// <summary>Samples fed per call; Vosk reports a finished utterance after any of them.</summary>
    private const int ChunkSamples = 8_000;

    private readonly InFlightTracker _inFlight = new();
    private Model? _model;

    public string EngineName => "vosk";

    public IReadOnlySet<string> SupportedLanguages { get; private set; } = new HashSet<string>();

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!Directory.Exists(settings.ModelPath))
        {
            throw new DirectoryNotFoundException($"Vosk model directory not found: {settings.ModelPath}");
        }

        global::Vosk.Vosk.SetLogLevel(-1); // Kaldi's own logging bypasses ours; failures still surface as exceptions.
        _model = new Model(settings.ModelPath);
        SupportedLanguages = new HashSet<string>(StringComparer.Ordinal) { settings.ModelLanguage };
        LogLoaded(settings.ModelPath, settings.ModelLanguage);
        return Task.CompletedTask;
    }

    public async Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var model = _model ?? throw new InvalidOperationException("The model is not loaded.");

        using var running = _inFlight.Enter();
        // Vosk decodes synchronously; run it off the caller's thread so cancellation and shutdown stay responsive.
        var words = await Task.Run(() => Recognize(model, audio, cancellationToken), cancellationToken).ConfigureAwait(false);
        return TranscriptionOutput.FromSegments(words, audio.Duration);
    }

    // Native decoding cannot be interrupted: free the model only once no transcription uses it.
    public ValueTask DisposeAsync() => _inFlight.DisposeWhenIdleAsync(() =>
    {
        _model?.Dispose();
        _model = null;
    });

    /// <summary>
    /// Feeds 16-bit samples in chunks. Vosk returns each finished utterance from <c>Result()</c> and
    /// only the last one from <c>FinalResult()</c>, so both are collected.
    /// </summary>
    private static List<TranscriptSegment> Recognize(Model model, AudioSamples audio, CancellationToken cancellationToken)
    {
        using var recognizer = new VoskRecognizer(model, audio.SampleRate);
        recognizer.SetWords(true);

        var words = new List<TranscriptSegment>();
        var chunk = new short[ChunkSamples];
        for (var offset = 0; offset < audio.Samples.Length; offset += ChunkSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(ChunkSamples, audio.Samples.Length - offset);
            for (var i = 0; i < length; i++)
            {
                chunk[i] = (short)Math.Clamp(audio.Samples[offset + i] * short.MaxValue, short.MinValue, short.MaxValue);
            }

            if (recognizer.AcceptWaveform(chunk, length))
            {
                words.AddRange(ParseWords(recognizer.Result()));
            }
        }

        words.AddRange(ParseWords(recognizer.FinalResult()));
        return words;
    }

    private static IEnumerable<TranscriptSegment> ParseWords(string json) =>
        (JsonSerializer.Deserialize<VoskResult>(json)?.Words ?? [])
            .Select(word => new TranscriptSegment(word.Text, TimeSpan.FromSeconds(word.Start), TimeSpan.FromSeconds(word.End)));

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded Vosk model {ModelPath} ({Language})")]
    private partial void LogLoaded(string modelPath, string language);

    private sealed record VoskResult([property: JsonPropertyName("result")] IReadOnlyList<VoskWord>? Words);

    private sealed record VoskWord(
        [property: JsonPropertyName("word")] string Text,
        [property: JsonPropertyName("start")] double Start,
        [property: JsonPropertyName("end")] double End);
}
