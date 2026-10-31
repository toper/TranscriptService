using TranscriptService.Contracts.Http;

namespace TranscriptService.Demo.Web.Client.Transcriptions;

/// <summary>Messages the server pushes to a browser over the transcription hub.</summary>
public interface ITranscriptionClient
{
    /// <summary>A submitted utterance or file was transcribed, or failed.</summary>
    Task TranscriptionFinished(TranscriptionResponse transcription);
}

/// <summary>Endpoint paths shared by the server and the browser.</summary>
public static class DemoRoutes
{
    public const string Hub = "/hubs/transcriptions";
    public const string Utterances = "/utterances";
    public const string Callbacks = "/callbacks/transcriptions";

    /// <summary>Form field carrying the SignalR connection that should receive the result.</summary>
    public const string ConnectionIdField = "connectionId";
}
