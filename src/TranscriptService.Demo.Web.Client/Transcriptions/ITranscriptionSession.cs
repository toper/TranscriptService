using TranscriptService.Contracts.Http;

namespace TranscriptService.Demo.Web.Client.Transcriptions;

/// <summary>The page's connection to the demo backend: submit recordings, receive results as they come.</summary>
public interface ITranscriptionSession : IAsyncDisposable
{
    /// <summary>Raised for every finished transcription submitted through this session.</summary>
    event Func<TranscriptionResponse, Task>? Finished;

    /// <summary>Connects to the hub; results can only be routed back once connected.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Submits a WAV recording.</summary>
    /// <returns>The transcription job id.</returns>
    Task<Guid> SubmitAsync(Stream wav, string fileName, CancellationToken cancellationToken);
}
