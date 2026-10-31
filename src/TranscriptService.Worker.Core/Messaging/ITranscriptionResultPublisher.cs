namespace TranscriptService.Worker.Core.Messaging;

/// <summary>Reports the outcome of a transcription job back to the API.</summary>
public interface ITranscriptionResultPublisher
{
    /// <summary>Publishes a result event (<c>TranscriptionCompleted</c> or <c>TranscriptionFailed</c>).</summary>
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken)
        where TMessage : class;
}
