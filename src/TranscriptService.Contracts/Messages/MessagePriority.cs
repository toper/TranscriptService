namespace TranscriptService.Contracts.Messages;

/// <summary>
/// RabbitMQ priorities of transcription requests. Short recordings (for example live demo
/// utterances) jump ahead of long phone calls so they are not stuck behind them.
/// </summary>
public static class MessagePriority
{
    public const byte Normal = 1;

    public const byte High = 2;

    /// <summary>
    /// Highest priority the request queue is declared with (<c>x-max-priority</c>). RabbitMQ keeps
    /// internal structures per level and the value cannot change after declaration, so it stays minimal.
    /// </summary>
    public const byte Max = High;

    /// <summary>Chooses the priority of a request for a recording of the given duration.</summary>
    public static byte For(TimeSpan audioDuration, TimeSpan shortAudioThreshold) =>
        audioDuration <= shortAudioThreshold ? High : Normal;
}
