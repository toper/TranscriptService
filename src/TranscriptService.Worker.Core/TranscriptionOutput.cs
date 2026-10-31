using TranscriptService.Contracts;

namespace TranscriptService.Worker.Core;

/// <summary>What an engine recognized in a recording.</summary>
public sealed record TranscriptionOutput(string FullText, IReadOnlyList<TranscriptSegment> Segments)
{
    /// <summary>
    /// Builds the output from an engine's raw segments: trims them, drops empty ones and the
    /// bracketed captions Whisper models emit for silence, music and noise (<c>[BLANK_AUDIO]</c>,
    /// <c>(muzyka)</c>) — on call recordings with hold music they are not part of what was said.
    /// </summary>
    public static TranscriptionOutput FromSegments(IEnumerable<TranscriptSegment> segments)
    {
        var spoken = segments
            .Select(segment => segment with { Text = segment.Text.Trim() })
            .Where(segment => IsSpeech(segment.Text))
            .ToList();
        return new TranscriptionOutput(string.Join(' ', spoken.Select(segment => segment.Text)), spoken);
    }

    private static bool IsSpeech(string text) =>
        text.Length > 0
        && !(text.StartsWith('[') && text.EndsWith(']'))
        && !(text.StartsWith('(') && text.EndsWith(')'));
}
