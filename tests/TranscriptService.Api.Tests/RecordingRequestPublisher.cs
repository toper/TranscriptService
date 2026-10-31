using TranscriptService.Api.Messaging;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Tests;

/// <summary>Captures published transcription requests instead of sending them to RabbitMQ.</summary>
public sealed class RecordingRequestPublisher : ITranscriptionRequestPublisher
{
    private readonly List<(TranscribeAudio Command, byte Priority)> _published = [];

    public IReadOnlyList<(TranscribeAudio Command, byte Priority)> Published => _published;

    public Exception? Fail { get; set; }

    public Task PublishAsync(TranscribeAudio command, byte priority, CancellationToken cancellationToken)
    {
        if (Fail is not null)
        {
            return Task.FromException(Fail);
        }

        _published.Add((command, priority));
        return Task.CompletedTask;
    }
}
