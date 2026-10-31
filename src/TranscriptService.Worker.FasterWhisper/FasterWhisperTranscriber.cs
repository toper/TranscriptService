using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Qourex.FasterWhisper.NET;
using TranscriptService.Audio;
using TranscriptService.Contracts;
using TranscriptService.Worker.Core;

namespace TranscriptService.Worker.FasterWhisper;

/// <summary>
/// FasterWhisper.NET engine: Whisper on CTranslate2, loaded once from a model directory. Supported
/// languages come from the model itself (its <c>lang_ids</c>), so the check always matches the
/// deployed model, English-only ones included.
/// </summary>
public sealed partial class FasterWhisperTranscriber(
    IOptions<FasterWhisperOptions> options,
    ILogger<FasterWhisperTranscriber> logger) : ITranscriber
{
    private readonly InFlightTracker _inFlight = new();
    private WhisperModel? _model;

    public string EngineName => "fasterwhisper";

    public IReadOnlySet<string> SupportedLanguages { get; private set; } = new HashSet<string>();

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!Directory.Exists(settings.ModelPath))
        {
            throw new DirectoryNotFoundException($"CTranslate2 model directory not found: {settings.ModelPath}");
        }

        SupportedLanguages = await ReadLanguagesAsync(settings.ModelPath, cancellationToken).ConfigureAwait(false);

        var device = settings.Device == ComputeDevice.Cuda ? "cuda" : "cpu";
        _model = await WhisperModel.LoadAsync(
            settings.ModelPath,
            device,
            settings.EffectiveComputeType,
            cpuThreads: settings.CpuThreads,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        LogLoaded(settings.ModelPath, device, settings.EffectiveComputeType);
    }

    public async Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var model = _model ?? throw new InvalidOperationException("The model is not loaded.");
        var settings = options.Value;

        using var running = _inFlight.Enter();
        var segments = new List<TranscriptSegment>();
        var recognized = model.TranscribeAsync(
            audio.Samples,
            language,
            options: new WhisperOptions { BeamSize = settings.BeamSize },
            vadOptions: new VadOptions { Enabled = settings.VadEnabled },
            cancellationToken: cancellationToken);
        await foreach (var segment in recognized.ConfigureAwait(false))
        {
            segments.Add(new TranscriptSegment(segment.Text, TimeSpan.FromSeconds(segment.Start), TimeSpan.FromSeconds(segment.End)));
        }

        return TranscriptionOutput.FromSegments(segments, audio.Duration);
    }

    // Native decoding cannot be interrupted: free the model only once no transcription uses it.
    public ValueTask DisposeAsync() => _inFlight.DisposeWhenIdleAsync(() =>
    {
        _model?.Dispose();
        _model = null;
    });

    /// <summary>
    /// Languages of a CTranslate2 Whisper model: the vocabulary tokens listed in <c>lang_ids</c> of
    /// <c>config.json</c>. English-only models have no <c>lang_ids</c>, even when they keep the
    /// multilingual vocabulary. Older conversions store the vocabulary as <c>vocabulary.txt</c>.
    /// </summary>
    internal static async Task<IReadOnlySet<string>> ReadLanguagesAsync(string modelDirectory, CancellationToken cancellationToken)
    {
        var config = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(modelDirectory, "config.json"), cancellationToken).ConfigureAwait(false));
        var languageIds = config?["lang_ids"]?.AsArray().Select(id => id!.GetValue<int>()).ToList() ?? [];
        if (languageIds.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal) { "en" };
        }

        var vocabulary = await ReadVocabularyAsync(modelDirectory, cancellationToken).ConfigureAwait(false);
        return languageIds
            .Select(id => LanguageToken().Match(vocabulary[id]))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task<IReadOnlyList<string>> ReadVocabularyAsync(string modelDirectory, CancellationToken cancellationToken)
    {
        var json = Path.Combine(modelDirectory, "vocabulary.json");
        if (!File.Exists(json))
        {
            return await File.ReadAllLinesAsync(Path.Combine(modelDirectory, "vocabulary.txt"), cancellationToken).ConfigureAwait(false);
        }

        var stream = File.OpenRead(json);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<string[]>(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? [];
        }
    }

    [GeneratedRegex(@"\A<\|([a-z]{2,3})\|>\z")]
    private static partial Regex LanguageToken();

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded CTranslate2 model {ModelPath} on {Device} ({ComputeType})")]
    private partial void LogLoaded(string modelPath, string device, string computeType);
}
