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

    /// <summary>
    /// Language codes the loaded model recognizes. <see cref="TranscriptionJobHandler"/> rejects other
    /// languages before calling <see cref="TranscribeAsync"/>, so engines need not check again.
    /// </summary>
    IReadOnlySet<string> SupportedLanguages { get; }

    /// <summary>Loads the model. Throws when the configuration cannot work, so the host fails to start.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Transcribes 16 kHz mono samples.</summary>
    /// <exception cref="TranscriptionRejectedException">The engine cannot serve the request.</exception>
    Task<TranscriptionOutput> TranscribeAsync(AudioSamples audio, string language, CancellationToken cancellationToken);
}
