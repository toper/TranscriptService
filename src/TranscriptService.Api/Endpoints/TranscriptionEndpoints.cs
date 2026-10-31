using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Messaging;
using TranscriptService.Api.Options;
using TranscriptService.Audio;
using TranscriptService.Contracts.Http;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Endpoints;

/// <summary>Endpoints under <c>/api/v1/transcriptions</c>.</summary>
internal static partial class TranscriptionEndpoints
{
    private const string DefaultLanguage = "pl";

    public static RouteGroupBuilder MapTranscriptionEndpoints(this RouteGroupBuilder api)
    {
        var transcriptions = api.MapGroup("/transcriptions").WithTags("Transcriptions");

        transcriptions.MapPost("/", CreateAsync)
            .WithName("CreateTranscription")
            .WithSummary("Submit a recording for transcription")
            .WithDescription(
                "Accepts a WAV recording (PCM 16-bit or G.711 μ-law/A-law, any sample rate, mono or stereo) and queues it. " +
                "Returns immediately with the job; poll its Location or pass `callbackUrl` to receive the result as a signed POST " +
                "(header `X-Signature: sha256=<HMAC of the body>`). Delivered results are deleted; others expire after the configured TTL.")
            .Accepts<IFormFile>("multipart/form-data")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .DisableAntiforgery();

        transcriptions.MapGet("/{id:guid}", GetAsync)
            .WithName("GetTranscription")
            .WithSummary("Get a transcription job")
            .WithDescription("Returns the job status and, once completed, the transcript with timed segments.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        transcriptions.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteTranscription")
            .WithSummary("Delete a transcription job")
            .WithDescription("Deletes the job, its result and the stored recording.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return api;
    }

    private static async Task<Results<Accepted<TranscriptionResponse>, ProblemHttpResult, ValidationProblem>> CreateAsync(
        IFormFile? audio,
        [FromForm] string? language,
        [FromForm] string? callbackUrl,
        IOptions<ApiOptions> options,
        TranscriptionDbContext db,
        AudioStorage storage,
        ITranscriptionRequestPublisher publisher,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        language ??= DefaultLanguage;

        var errors = new Dictionary<string, string[]>();
        if (audio is null || audio.Length == 0)
        {
            errors["audio"] = ["A WAV recording is required."];
        }

        if (!LanguageCode().IsMatch(language))
        {
            errors["language"] = ["Use a two-letter lowercase language code, e.g. 'pl'."];
        }

        Uri? callback = null;
        if (callbackUrl is not null && (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out callback) || (callback.Scheme != Uri.UriSchemeHttp && callback.Scheme != Uri.UriSchemeHttps)))
        {
            errors["callbackUrl"] = ["Use an absolute http or https URL."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (audio!.Length > settings.MaxUploadBytes)
        {
            return TypedResults.Problem(
                $"The recording is {audio.Length} bytes; the limit is {settings.MaxUploadBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Recording too large");
        }

        AudioFormatInfo format;
        try
        {
            using var stream = audio.OpenReadStream();
            format = AudioInspector.Inspect(stream);
        }
        catch (UnsupportedAudioException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Unsupported recording");
        }

        if (format.Duration > settings.MaxAudioDuration)
        {
            return TypedResults.Problem(
                $"The recording lasts {format.Duration:hh\\:mm\\:ss}; the limit is {settings.MaxAudioDuration:hh\\:mm\\:ss}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Recording too long");
        }

        var jobId = Guid.CreateVersion7();
        var job = new TranscriptionJob
        {
            Id = jobId,
            Status = JobStatus.Queued,
            Language = language,
            AudioPath = storage.PathFor(jobId),
            AudioDuration = format.Duration,
            CallbackUrl = callback,
            CreatedAt = time.GetUtcNow(),
        };

        // Nothing may outlive a failed or aborted request: no recording without a job, no job without a queued request.
        try
        {
            using var stream = audio.OpenReadStream();
            await storage.SaveAsync(jobId, stream, cancellationToken).ConfigureAwait(false);
            db.Jobs.Add(job);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            storage.Delete(job.AudioPath);
            throw;
        }

        try
        {
            var command = new TranscribeAudio(jobId, job.AudioPath, language, format.Duration);
            await publisher.PublishAsync(command, MessagePriority.For(format.Duration, settings.HighPriorityThreshold), cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any publishing failure means the job cannot run; undo it and tell the client to retry.
        catch (Exception)
#pragma warning restore CA1031
        {
            db.Jobs.Remove(job);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            storage.Delete(job.AudioPath);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return TypedResults.Problem(
                "The transcription queue is unavailable. Retry later.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Queue unavailable");
        }

        return TypedResults.Accepted($"/api/v1/transcriptions/{jobId}", job.ToResponse());
    }

    private static async Task<Results<Ok<TranscriptionResponse>, NotFound>> GetAsync(
        Guid id,
        TranscriptionDbContext db,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, cancellationToken).ConfigureAwait(false);
        return job is null ? TypedResults.NotFound() : TypedResults.Ok(job.ToResponse());
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        Guid id,
        TranscriptionDbContext db,
        AudioStorage storage,
        CancellationToken cancellationToken)
    {
        var job = await db.Jobs.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return TypedResults.NotFound();
        }

        db.Jobs.Remove(job);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        storage.Delete(job.AudioPath);
        return TypedResults.NoContent();
    }

    [GeneratedRegex(@"\A[a-z]{2}\z")]
    private static partial Regex LanguageCode();
}
