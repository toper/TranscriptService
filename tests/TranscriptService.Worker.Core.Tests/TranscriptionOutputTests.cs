using TranscriptService.Contracts;

namespace TranscriptService.Worker.Core.Tests;

public sealed class TranscriptionOutputTests
{
    private static readonly TimeSpan LongRecording = TimeSpan.FromMinutes(1);

    [Fact]
    public void FromSegments_SegmentEndingAfterTheRecording_IsDropped()
    {
        // Measured with FasterWhisper on a 3.3 s call: a closing phrase "heard" in the padded silence.
        var output = TranscriptionOutput.FromSegments(
            [Segment("Od jakiej wagi gabarytowej liczy się cena za paletę?", 0, 3.1), Segment("Dziękuje za uwagę!", 3.1, 3.6)],
            TimeSpan.FromSeconds(3.3));

        output.FullText.Should().Be("Od jakiej wagi gabarytowej liczy się cena za paletę?");
    }

    [Fact]
    public void FromSegments_LastSegmentEndingWithinTimestampPrecision_IsKept()
    {
        var output = TranscriptionOutput.FromSegments([Segment("Dzień dobry.", 0, 3.4)], TimeSpan.FromSeconds(3.32));

        output.Segments.Should().ContainSingle();
    }

    [Fact]
    public void FromSegments_DropsNonSpeechCaptionsAndBlanks_AndJoinsText()
    {
        var output = TranscriptionOutput.FromSegments(
        [
            Segment(" Dzień dobry. ", 0, 1),
            Segment("[BLANK_AUDIO]", 1, 4),
            Segment("(muzyka)", 4, 9),
            Segment("  ", 9, 10),
            Segment("Paleta (euro) kosztuje 100 zł.", 10, 12),
        ],
        LongRecording);

        output.FullText.Should().Be("Dzień dobry. Paleta (euro) kosztuje 100 zł.");
        output.Segments.Select(s => s.Text).Should().Equal("Dzień dobry.", "Paleta (euro) kosztuje 100 zł.");
    }

    private static TranscriptSegment Segment(string text, double start, double end) =>
        new(text, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));
}
