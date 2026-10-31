using TranscriptService.Contracts;

namespace TranscriptService.Worker.Core.Tests;

public sealed class TranscriptionOutputTests
{
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
        ]);

        output.FullText.Should().Be("Dzień dobry. Paleta (euro) kosztuje 100 zł.");
        output.Segments.Select(s => s.Text).Should().Equal("Dzień dobry.", "Paleta (euro) kosztuje 100 zł.");
    }

    private static TranscriptSegment Segment(string text, int start, int end) =>
        new(text, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end));
}
