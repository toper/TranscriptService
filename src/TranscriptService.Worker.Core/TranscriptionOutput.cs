using TranscriptService.Contracts;

namespace TranscriptService.Worker.Core;

/// <summary>What an engine recognized in a recording.</summary>
public sealed record TranscriptionOutput(string FullText, IReadOnlyList<TranscriptSegment> Segments)
{
    /// <summary>Timestamps come in 20 ms steps and segment ends round up; allow that much overrun.</summary>
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Builds the output from an engine's raw segments, keeping only what was said in the recording:
    /// <list type="bullet">
    /// <item>empty segments and bracketed captions for silence, music and noise (<c>[BLANK_AUDIO]</c>,
    /// <c>(muzyka)</c>) are dropped;</item>
    /// <item>segments ending after the recording are dropped: Whisper decodes 30-second windows padded
    /// with silence and tends to "hear" a closing phrase there ("Dziękuję za uwagę!").</item>
    /// </list>
    /// </summary>
    public static TranscriptionOutput FromSegments(IEnumerable<TranscriptSegment> segments, TimeSpan audioDuration)
    {
        var spoken = segments
            .Select(segment => segment with { Text = segment.Text.Trim() })
            .Where(segment => IsSpeech(segment.Text) && segment.End <= audioDuration + TimestampTolerance)
            .ToList();
        return new TranscriptionOutput(string.Join(' ', spoken.Select(segment => segment.Text)), spoken);
    }

    private static bool IsSpeech(string text) =>
        text.Length > 0
        && !(text.StartsWith('[') && text.EndsWith(']'))
        && !(text.StartsWith('(') && text.EndsWith(')'));
}
