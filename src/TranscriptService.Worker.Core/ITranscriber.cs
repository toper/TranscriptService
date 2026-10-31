using TranscriptService.Audio;

namespace TranscriptService.Worker.Core;

/// <summary>
/// A speech recognition engine. Each worker service registers exactly one implementation as a
/// singleton: the model is loaded once, at startup, and shared by every request.
/// </summary>
public interface ITranscriber : IAsyncDisposable
{
    /// <summary>Short engine identifier reported with results, e.g. <c>whispernet</c>.</summary>
    string EngineName { get; }

    /// <summary>Loads the model. Throws when the configuration cannot work, so the host fails to start.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Transcribes 16 kHz mono samples.</summary>
    /// <exception cref="TranscriptionRejectedException">The engine cannot serve the request, e.g. its language.</exception>
    Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken);
}
