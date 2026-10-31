using TranscriptService.Contracts;

namespace TranscriptService.Worker.Core;

/// <summary>What an engine recognized in a recording.</summary>
public sealed record TranscriptionOutput(string FullText, IReadOnlyList<TranscriptSegment> Segments)
{
    /// <summary>
    /// Builds the output from an engine's raw segments, keeping only what was said in the recording:
    /// <list type="bullet">
    /// <item>empty segments and bracketed captions for silence, music and noise (<c>[BLANK_AUDIO]</c>,
    /// <c>(muzyka)</c>) are dropped;</item>
    /// <item>segments lying mostly after the end of the recording are dropped: Whisper decodes 30-second
    /// windows padded with silence and tends to "hear" a closing phrase there ("Dziękuję za uwagę!").
    /// A segment mostly inside the recording is kept with its end clamped, so a call cut off
    /// mid-sentence keeps its last words.</item>
    /// </list>
    /// </summary>
    public static TranscriptionOutput FromSegments(IEnumerable<TranscriptSegment> segments, TimeSpan audioDuration)
    {
        var spoken = segments
            .Select(segment => segment with { Text = segment.Text.Trim() })
            .Where(segment => IsSpeech(segment.Text) && MostlyWithin(segment, audioDuration))
            .Select(segment => segment.End > audioDuration ? segment with { End = audioDuration } : segment)
            .ToList();
        return new TranscriptionOutput(string.Join(' ', spoken.Select(segment => segment.Text)), spoken);
    }

    private static bool MostlyWithin(TranscriptSegment segment, TimeSpan audioDuration)
    {
        var inside = (segment.End < audioDuration ? segment.End : audioDuration) - segment.Start;
        return inside >= (segment.End - segment.Start) / 2;
    }

    private static bool IsSpeech(string text) =>
        text.Length > 0
        && !(text.StartsWith('[') && text.EndsWith(']'))
        && !(text.StartsWith('(') && text.EndsWith(')'));
}
