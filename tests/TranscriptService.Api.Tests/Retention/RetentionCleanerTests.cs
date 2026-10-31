using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Options;
using TranscriptService.Api.Retention;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Tests.Retention;

public sealed class RetentionCleanerTests : IAsyncLifetime
{
    private static readonly ApiOptions Settings = new() { ResultTtl = TimeSpan.FromMinutes(15), MaxAudioDuration = TimeSpan.FromMinutes(30) };

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly string _storage = Directory.CreateTempSubdirectory("retention-").FullName;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = Context();
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task Clean_RemovesExpiredFinishedJobs_KeepsFresh()
    {
        var now = _time.GetUtcNow();
        var expired = await AddAsync(JobStatus.Completed, finishedAt: now - TimeSpan.FromMinutes(16));
        var expiredUndeliverable = await AddAsync(JobStatus.CallbackFailed, finishedAt: now - TimeSpan.FromMinutes(20));
        var fresh = await AddAsync(JobStatus.Failed, finishedAt: now - TimeSpan.FromMinutes(5));

        var removed = await CleanAsync();

        removed.Should().Be(2);
        (await IdsAsync()).Should().Equal(fresh.Id);
        File.Exists(expired.AudioPath).Should().BeFalse();
        File.Exists(expiredUndeliverable.AudioPath).Should().BeFalse();
    }

    [Fact]
    public async Task Clean_StaleQueuedJob_IsFailedAsTimedOut()
    {
        var now = _time.GetUtcNow();
        var stale = await AddAsync(JobStatus.Queued, createdAt: now - TimeSpan.FromHours(3));
        var waiting = await AddAsync(JobStatus.Queued, createdAt: now - TimeSpan.FromMinutes(10));

        await CleanAsync();

        await using var context = Context();
        var timedOut = await context.Jobs.SingleAsync(j => j.Id == stale.Id, TestContext.Current.CancellationToken);
        timedOut.Status.Should().Be(JobStatus.Failed);
        timedOut.FailureReason.Should().Contain("timed out");
        timedOut.FinishedAt.Should().Be(now);
        File.Exists(stale.AudioPath).Should().BeFalse();
        (await context.Jobs.SingleAsync(j => j.Id == waiting.Id, TestContext.Current.CancellationToken)).Status.Should().Be(JobStatus.Queued);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        Directory.Delete(_storage, recursive: true);
    }

    private async Task<int> CleanAsync()
    {
        await using var context = Context();
        var storage = new AudioStorage(Microsoft.Extensions.Options.Options.Create(new ApiOptions { StoragePath = _storage }));
        var cleaner = new RetentionCleaner(context, storage, new TranscriptService.Api.Callbacks.PendingCallbacks(), Microsoft.Extensions.Options.Options.Create(Settings), _time, NullLogger<RetentionCleaner>.Instance);
        return await cleaner.CleanAsync(TestContext.Current.CancellationToken);
    }

    private async Task<TranscriptionJob> AddAsync(JobStatus status, DateTimeOffset? finishedAt = null, DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(_storage, $"{id}.wav");
        await File.WriteAllBytesAsync(path, [0], TestContext.Current.CancellationToken);
        var job = new TranscriptionJob
        {
            Id = id,
            Status = status,
            Language = "pl",
            AudioPath = path,
            CreatedAt = createdAt ?? finishedAt ?? _time.GetUtcNow(),
            FinishedAt = finishedAt,
        };
        await using var context = Context();
        context.Jobs.Add(job);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return job;
    }

    private async Task<List<Guid>> IdsAsync()
    {
        await using var context = Context();
        return await context.Jobs.Select(j => j.Id).ToListAsync(TestContext.Current.CancellationToken);
    }

    private TranscriptionDbContext Context() =>
        new(new DbContextOptionsBuilder<TranscriptionDbContext>().UseSqlite(_connection).Options);
}
