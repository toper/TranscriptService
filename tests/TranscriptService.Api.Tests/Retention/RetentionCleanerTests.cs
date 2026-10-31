using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Options;
using TranscriptService.Api.Retention;
using TranscriptService.Api.Tests.Support;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Tests.Retention;

public sealed class RetentionCleanerTests : IAsyncLifetime
{
    private static readonly ApiOptions Settings = new() { ResultTtl = TimeSpan.FromMinutes(15), MaxAudioDuration = TimeSpan.FromMinutes(30) };

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly PendingCallbacks _callbacks = new();
    private SqliteJobStore _store = null!;

    private DateTimeOffset Now => _time.GetUtcNow();

    public async ValueTask InitializeAsync() => _store = await SqliteJobStore.CreateAsync();

    [Fact]
    public async Task Clean_RemovesExpiredFinishedJobs_KeepsFresh()
    {
        await _store.AddJobAsync(JobStatus.Completed, Now, finishedAt: Now - TimeSpan.FromMinutes(16));
        await _store.AddJobAsync(JobStatus.CallbackFailed, Now, finishedAt: Now - TimeSpan.FromMinutes(20), callback: true);
        var fresh = await _store.AddJobAsync(JobStatus.Failed, Now, finishedAt: Now - TimeSpan.FromMinutes(5));

        var removed = await CleanAsync();

        removed.Should().Be(2);
        (await _store.IdsAsync()).Should().Equal(fresh.Id);
    }

    [Fact]
    public async Task Clean_FinishedJobAwaitingCallback_IsKept()
    {
        var awaiting = await _store.AddJobAsync(JobStatus.Completed, Now, finishedAt: Now - TimeSpan.FromHours(1), callback: true);

        await CleanAsync();

        (await _store.IdsAsync()).Should().Equal(awaiting.Id);
    }

    [Fact]
    public async Task Clean_OrphanedRecordingOlderThanTtl_IsDeleted()
    {
        var orphan = await WriteRecordingAsync(writtenAt: Now - TimeSpan.FromHours(1));
        var fresh = await WriteRecordingAsync(writtenAt: Now);
        var queued = await _store.AddJobAsync(JobStatus.Queued, createdAt: Now - TimeSpan.FromHours(1));
        File.SetLastWriteTimeUtc(queued.AudioPath, (Now - TimeSpan.FromHours(1)).UtcDateTime);

        await CleanAsync();

        File.Exists(orphan).Should().BeFalse();
        File.Exists(fresh).Should().BeTrue("it may belong to an upload in progress");
        File.Exists(queued.AudioPath).Should().BeTrue("its job still needs it");
    }

    [Fact]
    public async Task Clean_StaleQueuedJob_IsFailedAsTimedOut_AndClientNotified()
    {
        var stale = await _store.AddJobAsync(JobStatus.Queued, createdAt: Now - TimeSpan.FromHours(3), callback: true);
        var waiting = await _store.AddJobAsync(JobStatus.Queued, createdAt: Now - TimeSpan.FromMinutes(10));

        await CleanAsync();

        var timedOut = await _store.FindAsync(stale.Id);
        timedOut!.Status.Should().Be(JobStatus.Failed);
        timedOut.FailureReason.Should().Contain("timed out");
        timedOut.FinishedAt.Should().Be(Now);
        File.Exists(stale.AudioPath).Should().BeFalse();
        _callbacks.TryDequeue(out var notified).Should().BeTrue();
        notified.Should().Be(stale.Id);
        (await _store.FindAsync(waiting.Id))!.Status.Should().Be(JobStatus.Queued);
    }

    public async ValueTask DisposeAsync() => await _store.DisposeAsync();

    private async Task<int> CleanAsync()
    {
        await using var context = _store.Context();
        var cleaner = new RetentionCleaner(context, _store.Storage(), _callbacks, Microsoft.Extensions.Options.Options.Create(Settings), _time, NullLogger<RetentionCleaner>.Instance);
        return await cleaner.CleanAsync(TestContext.Current.CancellationToken);
    }

    private async Task<string> WriteRecordingAsync(DateTimeOffset writtenAt)
    {
        var path = _store.Storage().PathFor(Guid.NewGuid());
        await File.WriteAllBytesAsync(path, [0], TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(path, writtenAt.UtcDateTime);
        return path;
    }
}
