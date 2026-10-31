namespace TranscriptService.Contracts.Http;

/// <summary>Lifecycle of a transcription job.</summary>
public enum JobStatus
{
    /// <summary>Accepted and waiting for, or being processed by, an engine worker.</summary>
    Queued,

    /// <summary>Transcript available.</summary>
    Completed,

    /// <summary>The recording could not be transcribed; see the failure reason.</summary>
    Failed,

    /// <summary>The result exists, but the callback could not be delivered after retries.</summary>
    CallbackFailed,
}
