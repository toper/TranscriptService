using System.Text.Json;
using TranscriptService.Contracts;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Contracts.Tests;

public sealed class MessageSerializationTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TranscriptionCompleted_RoundTripsThroughSystemTextJson()
    {
        var original = new TranscriptionCompleted(
            JobId: Guid.NewGuid(),
            Engine: "whispernet",
            FullText: "Dzień dobry",
            Segments:
            [
                new TranscriptSegment("Dzień", TimeSpan.Zero, TimeSpan.FromMilliseconds(420)),
                new TranscriptSegment("dobry", TimeSpan.FromMilliseconds(420), TimeSpan.FromMilliseconds(900)),
            ],
            AudioDuration: TimeSpan.FromSeconds(1.5),
            ProcessingTime: TimeSpan.FromMilliseconds(250));

        var json = JsonSerializer.Serialize(original, WebOptions);
        var restored = JsonSerializer.Deserialize<TranscriptionCompleted>(json, WebOptions);

        restored.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void TranscribeAudio_RoundTripsThroughSystemTextJson()
    {
        var original = new TranscribeAudio(Guid.NewGuid(), "/data/audio/job.wav", "pl", TimeSpan.FromMinutes(3));

        RoundTrip(original).Should().Be(original);
    }

    [Fact]
    public void TranscriptionFailed_RoundTripsThroughSystemTextJson()
    {
        var original = new TranscriptionFailed(Guid.NewGuid(), "vosk", "Expected PCM 16-bit, μ-law or A-law; got IEEE float.");

        RoundTrip(original).Should().Be(original);
    }

    private static T? RoundTrip<T>(T message) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(message, WebOptions), WebOptions);
}
