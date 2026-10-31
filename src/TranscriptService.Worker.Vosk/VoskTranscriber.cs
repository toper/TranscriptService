using System.Reflection;
using System.Runtime.InteropServices;
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
/// <remarks>
/// The Vosk 0.3.38 .NET binding performs no error checking: a model or recognizer the native library
/// failed to create is wrapped as a null handle, and using it crashes the process. Every native object
/// is therefore checked right after construction.
/// </remarks>
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

        // Kaldi writes its warnings and errors (e.g. which model file is missing) to stderr; keep them.
        global::Vosk.Vosk.SetLogLevel(0);
        var model = new Model(settings.ModelPath);
        if (IsNull(model))
        {
            throw new InvalidOperationException($"Vosk could not load a model from {settings.ModelPath}; see the Kaldi messages above.");
        }

        _model = model;
        SupportedLanguages = new HashSet<string>(StringComparer.Ordinal) { settings.ModelLanguage };
        LogLoaded(settings.ModelPath, settings.ModelLanguage);
        return Task.CompletedTask;
    }

    public async Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        using var running = _inFlight.Enter(); // before reading the model, so disposal cannot free it underneath
        var model = _model ?? throw new InvalidOperationException("The model is not loaded.");
        if (!SupportedLanguages.Contains(language))
        {
            throw new TranscriptionRejectedException($"The deployed Vosk model recognizes '{options.Value.ModelLanguage}' only; requested '{language}'.");
        }

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
        if (IsNull(recognizer))
        {
            throw new InvalidOperationException($"Vosk could not create a recognizer for {audio.SampleRate} Hz audio.");
        }

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

    /// <summary>
    /// Whether a Vosk object wraps a null native pointer. The binding keeps the pointer in a private
    /// <c>handle</c> field (<see cref="HandleRef"/>) and exposes no public way to check it.
    /// </summary>
    private static bool IsNull(object native)
    {
        var field = native.GetType().GetField("handle", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Unexpected Vosk binding: {native.GetType()} has no handle field.");
        return ((HandleRef)field.GetValue(native)!).Handle == IntPtr.Zero;
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
