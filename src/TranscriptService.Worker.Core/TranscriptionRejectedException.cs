namespace TranscriptService.Worker.Core;

/// <summary>
/// Thrown by an engine for a request it cannot serve by design, e.g. a language its model does
/// not know. The message is shown to API clients, so it must explain the rejection in their terms.
/// </summary>
public sealed class TranscriptionRejectedException : Exception
{
    public TranscriptionRejectedException(string message)
        : base(message)
    {
    }

    public TranscriptionRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
