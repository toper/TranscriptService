namespace TranscriptService.Contracts.Messages;

/// <summary>Names of queues shared by the API and the engine workers.</summary>
public static class QueueNames
{
    /// <summary>Work queue consumed by whichever engine worker is deployed.</summary>
    public const string TranscriptionRequests = "transcription.requests";
}
