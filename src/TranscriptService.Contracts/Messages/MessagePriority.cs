namespace TranscriptService.Contracts.Messages;

/// <summary>
/// RabbitMQ priorities of transcription requests. Short recordings (for example live demo
/// utterances) jump ahead of long phone calls so they are not stuck behind them.
/// </summary>
public static class MessagePriority
{
    /// <summary>Highest priority the request queue is declared with (<c>x-max-priority</c>).</summary>
    public const byte Max = 10;

    public const byte High = 5;

    public const byte Normal = 1;

    /// <summary>Chooses the priority of a request for a recording of the given duration.</summary>
    public static byte For(TimeSpan audioDuration, TimeSpan shortAudioThreshold) =>
        audioDuration <= shortAudioThreshold ? High : Normal;
}
