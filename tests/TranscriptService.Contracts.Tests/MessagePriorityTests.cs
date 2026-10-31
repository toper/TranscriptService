using TranscriptService.Contracts.Messages;

namespace TranscriptService.Contracts.Tests;

public sealed class MessagePriorityTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    [Fact]
    public void For_AudioAtThreshold_ReturnsHigh() =>
        MessagePriority.For(TimeSpan.FromSeconds(30), Threshold).Should().Be(MessagePriority.High);

    [Fact]
    public void For_AudioAboveThreshold_ReturnsNormal() =>
        MessagePriority.For(TimeSpan.FromSeconds(31), Threshold).Should().Be(MessagePriority.Normal);
}
