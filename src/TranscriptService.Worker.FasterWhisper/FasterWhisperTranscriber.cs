using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Qourex.FasterWhisper.NET;
using TranscriptService.Audio;
using TranscriptService.Contracts;
using TranscriptService.Worker.Core;

namespace TranscriptService.Worker.FasterWhisper;

/// <summary>
/// FasterWhisper.NET engine: Whisper on CTranslate2, loaded once from a model directory. The
/// supported languages are read from the model's own vocabulary (its <c>&lt;|xx|&gt;</c> tokens), so
/// the check always matches the deployed model.
/// </summary>
public sealed partial class FasterWhisperTranscriber(
    IOptions<FasterWhisperOptions> options,
    ILogger<FasterWhisperTranscriber> logger) : ITranscriber
{
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

        SupportedLanguages = await ReadLanguagesAsync(Path.Combine(settings.ModelPath, "vocabulary.json"), cancellationToken).ConfigureAwait(false);

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

    public ValueTask DisposeAsync()
    {
        _model?.Dispose();
        _model = null;
        return ValueTask.CompletedTask;
    }

    private static async Task<IReadOnlySet<string>> ReadLanguagesAsync(string vocabularyPath, CancellationToken cancellationToken)
    {
        var stream = File.OpenRead(vocabularyPath);
        await using (stream.ConfigureAwait(false))
        {
            var tokens = await JsonSerializer.DeserializeAsync<string[]>(stream, cancellationToken: cancellationToken).ConfigureAwait(false) ?? [];
            return tokens
                .Select(token => LanguageToken().Match(token))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    [GeneratedRegex(@"\A<\|([a-z]{2,3})\|>\z")]
    private static partial Regex LanguageToken();

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded CTranslate2 model {ModelPath} on {Device} ({ComputeType})")]
    private partial void LogLoaded(string modelPath, string device, string computeType);
}
