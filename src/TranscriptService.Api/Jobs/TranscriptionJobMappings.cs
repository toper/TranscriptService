using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Jobs;

internal static class TranscriptionJobMappings
{
    public static TranscriptionResponse ToResponse(this TranscriptionJob job) => new(
        job.Id,
        job.Status,
        job.Language,
        job.CreatedAt,
        job.FinishedAt,
        job.Engine,
        job.FullText,
        job.Segments,
        job.FailureReason,
        job.AudioDuration,
        job.ProcessingTime);
}
