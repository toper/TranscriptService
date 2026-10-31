namespace TranscriptService.Api.Endpoints;

/// <summary>Endpoints under <c>/api/v1/transcriptions</c>.</summary>
internal static class TranscriptionEndpoints
{
    public static RouteGroupBuilder MapTranscriptionEndpoints(this RouteGroupBuilder api)
    {
        var transcriptions = api.MapGroup("/transcriptions").WithTags("Transcriptions");
        transcriptions.MapGet("/{id:guid}", (Guid id) => TypedResults.NotFound());
        return api;
    }
}
