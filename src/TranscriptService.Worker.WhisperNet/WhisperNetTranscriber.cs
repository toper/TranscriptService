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
    private readonly InFlightTracker _inFlight = new();
    private WhisperFactory? _factory;

    public string EngineName => "whispernet";

    public IReadOnlySet<string> SupportedLanguages { get; private set; } = new HashSet<string>();

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
        SupportedLanguages = WhisperFactory.GetSupportedLanguages().ToHashSet(StringComparer.Ordinal);

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
        using var running = _inFlight.Enter(); // before reading the model, so disposal cannot free it underneath
        var factory = _factory ?? throw new InvalidOperationException("The model is not loaded.");

        var processor = factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(options.Value.Threads)
            .Build();
        await using (processor.ConfigureAwait(false))
        {
            var segments = new List<TranscriptSegment>();
            await foreach (var segment in processor.ProcessAsync(audio.Samples, cancellationToken).ConfigureAwait(false))
            {
                segments.Add(new TranscriptSegment(segment.Text, segment.Start, segment.End));
            }

            return TranscriptionOutput.FromSegments(segments, audio.Duration);
        }
    }

    // Native decoding cannot be interrupted: free the model only once no transcription uses it.
    public ValueTask DisposeAsync() => _inFlight.DisposeWhenIdleAsync(() =>
    {
        _factory?.Dispose();
        _factory = null;
    });

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded GGML model {ModelPath} on the {Runtime} runtime")]
    private partial void LogLoaded(string modelPath, RuntimeLibrary? runtime);
}
