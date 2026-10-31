using TranscriptService.Contracts;

namespace TranscriptService.Worker.Core;

/// <summary>What an engine recognized in a recording.</summary>
public sealed record TranscriptionOutput(string FullText, IReadOnlyList<TranscriptSegment> Segments);
