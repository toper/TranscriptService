using TranscriptService.Contracts.Http;

namespace TranscriptService.Demo.Web.Client.Transcriptions;

/// <summary>Messages the server pushes to a browser over the transcription hub.</summary>
public interface ITranscriptionClient
{
    /// <summary>A submitted utterance or file was transcribed, or failed.</summary>
    Task TranscriptionFinished(TranscriptionResponse transcription);
}

/// <summary>Routes and names shared by the server and the browser.</summary>
public static class DemoRoutes
{
    public const string Hub = "/hubs/transcriptions";
    public const string Utterances = "/utterances";
    public const string Callbacks = "/callbacks/transcriptions";

    /// <summary>
    /// A browser's session: a stable id it joins as a SignalR group (again after every reconnect, when
    /// its connection id changes) and that travels in the callback URL, so results find their way back.
    /// </summary>
    public const string SessionField = "session";

    /// <summary>Hub method a browser calls to receive the results of its session.</summary>
    public const string JoinSession = "JoinSession";

    /// <summary>Upload limit for the demo; the API accepts more.</summary>
    public const long MaxUploadBytes = 25 * 1024 * 1024;
}
