using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Options;
using TranscriptService.Contracts.Callbacks;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Callbacks;

/// <summary>
/// Delivers finished jobs to their callback URLs as signed POST requests. A delivered job is
/// deleted at once; one that still fails after the retries of the <see cref="HttpClientName"/>
/// client is marked <see cref="JobStatus.CallbackFailed"/> and left for retention cleanup.
/// </summary>
internal sealed partial class CallbackDispatcher(
    PendingCallbacks pending,
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IOptions<ApiOptions> apiOptions,
    IOptions<JsonOptions> jsonOptions,
    ILogger<CallbackDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "callbacks";

    private const int MaxConcurrentDeliveries = 4;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnqueueUndeliveredAsync(stoppingToken).ConfigureAwait(false);

        // A few deliveries run side by side, so one slow client does not hold up everyone else.
        await Parallel.ForEachAsync(
            pending.ReadAllAsync(stoppingToken),
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentDeliveries, CancellationToken = stoppingToken },
            DeliverAsync).ConfigureAwait(false);
    }

    /// <summary>Picks up results that were finished but not delivered before the API restarted.</summary>
    private async Task EnqueueUndeliveredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscriptionDbContext>();
        var undelivered = await db.Jobs
            .Where(j => j.CallbackUrl != null && (j.Status == JobStatus.Completed || j.Status == JobStatus.Failed))
            .Select(j => j.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var jobId in undelivered)
        {
            await pending.EnqueueAsync(jobId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask DeliverAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TranscriptionDbContext>();
        var job = await db.Jobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        if (job?.CallbackUrl is null || job.Status is not (JobStatus.Completed or JobStatus.Failed))
        {
            return;
        }

        if (await PostAsync(job, cancellationToken).ConfigureAwait(false))
        {
            db.Jobs.Remove(job);
            LogDelivered(jobId);
        }
        else
        {
            job.Status = JobStatus.CallbackFailed;
            LogUndeliverable(jobId, job.CallbackUrl);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PostAsync(TranscriptionJob job, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(job.ToResponse(), jsonOptions.Value.SerializerOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, job.CallbackUrl) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(CallbackSigner.SignatureHeader, CallbackSigner.Sign(body, apiOptions.Value.CallbackSigningSecret));
        request.Headers.Add(CallbackSigner.JobIdHeader, job.Id.ToString());

        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogDeliveryError(ex, job.Id);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Delivered result of job {JobId}; job deleted")]
    private partial void LogDelivered(Guid jobId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not deliver result of job {JobId} to {CallbackUrl} after retries")]
    private partial void LogUndeliverable(Guid jobId, Uri callbackUrl);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Callback request for job {JobId} failed")]
    private partial void LogDeliveryError(Exception exception, Guid jobId);
}
