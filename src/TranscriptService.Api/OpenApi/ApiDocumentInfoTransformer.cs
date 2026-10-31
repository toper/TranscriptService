using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace TranscriptService.Api.OpenApi;

/// <summary>Title and overview shown at the top of the Scalar reference.</summary>
internal sealed class ApiDocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "TranscriptService API";
        document.Info.Description =
            "Asynchronous transcription of telephone recordings: WAV with PCM 16-bit or G.711 μ-law/A-law samples, " +
            "any sample rate (8 kHz telephony included), mono or stereo. Submit a recording, then poll the job or " +
            "receive the result as a signed callback.";
        return Task.CompletedTask;
    }
}
