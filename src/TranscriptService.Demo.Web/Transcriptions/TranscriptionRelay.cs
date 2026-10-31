using Microsoft.AspNetCore.SignalR;
using TranscriptService.Contracts.Http;
using TranscriptService.Demo.Web.Client.Transcriptions;

namespace TranscriptService.Demo.Web.Transcriptions;

/// <summary>
/// Matches results from the API to the browsers waiting for them. A fast GPU worker can deliver a
/// result before the API's 202 response reaches this backend, so a result for a job nobody has
/// registered yet is kept briefly and delivered when the registration arrives.
/// </summary>
public sealed partial class TranscriptionRelay(
    IHubContext<TranscriptionHub, ITranscriptionClient> hub,
    TimeProvider time,
    ILogger<TranscriptionRelay> logger)
{
    private static readonly TimeSpan EarlyResultLifetime = TimeSpan.FromMinutes(5);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, string> _waiting = [];
    private readonly Dictionary<Guid, (TranscriptionResponse Result, DateTimeOffset Arrived)> _early = [];

    /// <summary>Records that a browser waits for a job, delivering its result if it already arrived.</summary>
    public Task RegisterAsync(Guid jobId, string connectionId)
    {
        TranscriptionResponse? early;
        lock (_gate)
        {
            if (_early.Remove(jobId, out var arrived))
            {
                early = arrived.Result;
            }
            else
            {
                _waiting[jobId] = connectionId;
                LogWaiting(jobId, connectionId);
                return Task.CompletedTask;
            }
        }

        LogDelivered(jobId, connectionId, early: true);
        return hub.Clients.Client(connectionId).TranscriptionFinished(early);
    }

    /// <summary>Pushes a result to the browser waiting for it, or keeps it until one registers.</summary>
    public Task DeliverAsync(TranscriptionResponse result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string? connectionId;
        lock (_gate)
        {
            if (!_waiting.Remove(result.Id, out connectionId))
            {
                PruneEarlyResults();
                _early[result.Id] = (result, time.GetUtcNow());
                LogKeptForLater(result.Id);
                return Task.CompletedTask;
            }
        }

        LogDelivered(result.Id, connectionId, early: false);
        return hub.Clients.Client(connectionId).TranscriptionFinished(result);
    }

    /// <summary>Forgets every job of a browser that went away.</summary>
    public void Forget(string connectionId)
    {
        lock (_gate)
        {
            foreach (var jobId in _waiting.Where(entry => entry.Value == connectionId).Select(entry => entry.Key).ToList())
            {
                _waiting.Remove(jobId);
            }
        }
    }

    private void PruneEarlyResults()
    {
        var expired = time.GetUtcNow() - EarlyResultLifetime;
        foreach (var jobId in _early.Where(entry => entry.Value.Arrived < expired).Select(entry => entry.Key).ToList())
        {
            _early.Remove(jobId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobId} awaited by connection {ConnectionId}")]
    private partial void LogWaiting(Guid jobId, string connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Result of job {JobId} arrived before its registration; kept")]
    private partial void LogKeptForLater(Guid jobId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pushed result of job {JobId} to connection {ConnectionId} (arrived early: {Early})")]
    private partial void LogDelivered(Guid jobId, string connectionId, bool early);
}
