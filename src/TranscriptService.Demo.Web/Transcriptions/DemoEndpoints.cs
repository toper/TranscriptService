using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using TranscriptService.Contracts.Callbacks;
using TranscriptService.Contracts.Http;
using TranscriptService.Demo.Web.Client.Transcriptions;

namespace TranscriptService.Demo.Web.Transcriptions;

/// <summary>The demo backend: forwards recordings to the API and relays its callbacks to browsers.</summary>
internal static class DemoEndpoints
{
    /// <summary>Upload limit for the demo; the API accepts more.</summary>
    private const long MaxUploadBytes = 25 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHub<TranscriptionHub>(DemoRoutes.Hub);

        // Same-origin calls from the demo page; the demo runs locally only (no antiforgery or auth).
        app.MapPost(DemoRoutes.Utterances, SubmitAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes));

        app.MapPost(DemoRoutes.Callbacks, ReceiveCallbackAsync);
        return app;
    }

    private static async Task<Results<Accepted<TranscriptionResponse>, ProblemHttpResult>> SubmitAsync(
        IFormFile audio,
        [FromForm(Name = DemoRoutes.SessionField)] Guid session,
        TranscriptApiClient api,
        CancellationToken cancellationToken)
    {
        try
        {
            using var recording = audio.OpenReadStream();
            var job = await api.SubmitAsync(recording, audio.FileName, session, cancellationToken).ConfigureAwait(false);
            return TypedResults.Accepted((string?)null, job);
        }
        catch (TranscriptApiException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: ex.StatusCode, title: "The Transcript API rejected the recording");
        }
    }

    /// <summary>
    /// Verifies the API's HMAC signature over the raw body, then pushes the result to the browser
    /// session named in the callback URL. Every verified callback is acknowledged, so the API never
    /// retries a delivered result.
    /// </summary>
    private static async Task<Results<Ok, ProblemHttpResult>> ReceiveCallbackAsync(
        HttpRequest request,
        [FromQuery(Name = DemoRoutes.SessionField)] Guid? session,
        IOptions<TranscriptApiOptions> options,
        IHubContext<TranscriptionHub, ITranscriptionClient> hub,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var body = buffer.ToArray();
        if (!CallbackSigner.Verify(body, request.Headers[CallbackSigner.SignatureHeader], options.Value.CallbackSigningSecret))
        {
            // A body keeps the response out of the page-oriented status code handling.
            return TypedResults.Problem("The X-Signature header does not match the body.", statusCode: StatusCodes.Status401Unauthorized, title: "Invalid callback signature");
        }

        if (session is { } target)
        {
            var transcription = JsonSerializer.Deserialize<TranscriptionResponse>(body, Json)!;
            await hub.Clients.Group(target.ToString()).TranscriptionFinished(transcription).ConfigureAwait(false);
        }

        return TypedResults.Ok();
    }
}
