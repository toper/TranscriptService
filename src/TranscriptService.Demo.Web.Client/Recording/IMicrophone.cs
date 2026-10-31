namespace TranscriptService.Demo.Web.Client.Recording;

/// <summary>Microphone that delivers speech one utterance at a time.</summary>
public interface IMicrophone : IAsyncDisposable
{
    /// <summary>Raised with each finished utterance as a 16 kHz mono PCM16 WAV.</summary>
    event Func<byte[], Task>? UtteranceRecorded;

    /// <summary>Asks for microphone access and starts listening.</summary>
    Task StartAsync();

    /// <summary>Stops listening; speech in progress is delivered as a last utterance.</summary>
    Task StopAsync();
}
