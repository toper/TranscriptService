using Microsoft.Extensions.Options;
using TranscriptService.Audio;
using TranscriptService.Contracts;
using TranscriptService.Worker.Core;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace TranscriptService.Worker.WhisperNet;

/// <summary>
/// Whisper.net engine. The GGML model is loaded once into a <see cref="WhisperFactory"/>; each job
/// gets its own <see cref="WhisperProcessor"/>, because a processor is not safe to share between
/// concurrent transcriptions while the factory (and the model weights) is.
/// </summary>
public sealed partial class WhisperNetTranscriber(
    IOptions<WhisperNetOptions> options,
    ILogger<WhisperNetTranscriber> logger) : ITranscriber
{
    private WhisperFactory? _factory;
    private HashSet<string> _languages = [];

    public string EngineName => "whispernet";

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!File.Exists(settings.ModelPath))
        {
            throw new FileNotFoundException("GGML model not found.", settings.ModelPath);
        }

        // An explicit runtime list: a CUDA deployment must fail when CUDA is missing, not quietly run on the CPU.
        RuntimeOptions.RuntimeLibraryOrder = settings.Device == ComputeDevice.Cuda
            ? [RuntimeLibrary.Cuda]
            : [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
        _factory = WhisperFactory.FromPath(settings.ModelPath);
        _languages = [.. WhisperFactory.GetSupportedLanguages()];

        if (settings.Device == ComputeDevice.Cuda && RuntimeOptions.LoadedLibrary != RuntimeLibrary.Cuda)
        {
            throw new InvalidOperationException($"Device is Cuda, but Whisper.net loaded the {RuntimeOptions.LoadedLibrary} runtime.");
        }

        LogLoaded(settings.ModelPath, RuntimeOptions.LoadedLibrary);
        return Task.CompletedTask;
    }

    public async Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var factory = _factory ?? throw new InvalidOperationException("The model is not loaded.");
        if (!_languages.Contains(language))
        {
            throw new TranscriptionRejectedException($"Whisper does not support the language '{language}'.");
        }

        var processor = factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(options.Value.Threads)
            .Build();
        await using (processor.ConfigureAwait(false))
        {
            var segments = new List<TranscriptSegment>();
            await foreach (var segment in processor.ProcessAsync(audio.Samples, cancellationToken).ConfigureAwait(false))
            {
                var text = segment.Text.Trim();
                if (IsSpeech(text))
                {
                    segments.Add(new TranscriptSegment(text, segment.Start, segment.End));
                }
            }

            return new TranscriptionOutput(string.Join(' ', segments.Select(s => s.Text)), segments);
        }
    }

    /// <summary>
    /// Whisper marks silence, music and noise with bracketed captions such as <c>[BLANK_AUDIO]</c> or
    /// <c>(muzyka)</c>; on call recordings with hold music they are not part of what was said.
    /// </summary>
    internal static bool IsSpeech(string text) =>
        text.Length > 0
        && !(text.StartsWith('[') && text.EndsWith(']'))
        && !(text.StartsWith('(') && text.EndsWith(')'));

    public ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _factory = null;
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded GGML model {ModelPath} on the {Runtime} runtime")]
    private partial void LogLoaded(string modelPath, RuntimeLibrary? runtime);
}
