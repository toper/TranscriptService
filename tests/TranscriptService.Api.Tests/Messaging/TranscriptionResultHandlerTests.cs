using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Messaging;
using TranscriptService.Api.Options;
using TranscriptService.Contracts;
using TranscriptService.Contracts.Http;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Tests.Messaging;

public sealed class TranscriptionResultHandlerTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly PendingCallbacks _callbacks = new();
    private readonly string _storage = Directory.CreateTempSubdirectory("results-").FullName;

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var context = Context();
        await context.Database.MigrateAsync();
    }

    [Fact]
    public async Task OnCompleted_UpdatesJobDeletesRecording_AndEnqueuesCallback()
    {
        var job = await AddJobAsync(callback: true);

        await HandleAsync(Completed(job.Id));

        var stored = await FindAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Completed);
        stored.FullText.Should().Be("dzień dobry");
        stored.Engine.Should().Be("whispernet");
        stored.Segments.Should().ContainSingle();
        stored.FinishedAt.Should().Be(_time.GetUtcNow());
        File.Exists(job.AudioPath).Should().BeFalse("the recording is not needed once transcribed");
        (await DrainCallbacksAsync()).Should().Equal(job.Id);
    }

    [Fact]
    public async Task OnCompleted_NoCallbackUrl_DoesNotEnqueue()
    {
        var job = await AddJobAsync(callback: false);

        await HandleAsync(Completed(job.Id));

        (await DrainCallbacksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task OnFailed_StoresReason_DeletesRecording_AndEnqueuesCallback()
    {
        var job = await AddJobAsync(callback: true);

        await HandleAsync(new TranscriptionFailed(job.Id, "vosk", "Unsupported language."));

        var stored = await FindAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Failed);
        stored.FailureReason.Should().Be("Unsupported language.");
        File.Exists(job.AudioPath).Should().BeFalse();
        (await DrainCallbacksAsync()).Should().Equal(job.Id);
    }

    [Fact]
    public async Task OnCompleted_UnknownJob_IsIgnored()
    {
        await FluentActions.Awaiting(() => HandleAsync(Completed(Guid.NewGuid()))).Should().NotThrowAsync();

        (await DrainCallbacksAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task OnCompleted_Duplicate_DoesNotEnqueueSecondCallback()
    {
        var job = await AddJobAsync(callback: true);

        await HandleAsync(Completed(job.Id));
        await HandleAsync(Completed(job.Id) with { FullText = "redelivered" });

        (await FindAsync(job.Id))!.FullText.Should().Be("dzień dobry");
        (await DrainCallbacksAsync()).Should().Equal(job.Id);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        Directory.Delete(_storage, recursive: true);
    }

    private static TranscriptionCompleted Completed(Guid jobId) => new(
        jobId,
        "whispernet",
        "dzień dobry",
        [new TranscriptSegment("dzień dobry", TimeSpan.Zero, TimeSpan.FromSeconds(1))],
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMilliseconds(300));

    private async Task HandleAsync<TMessage>(TMessage message)
        where TMessage : class
    {
        await using var context = Context();
        var handler = new TranscriptionResultHandler(context, Storage(), _callbacks, _time, NullLogger<TranscriptionResultHandler>.Instance);
        await (message switch
        {
            TranscriptionCompleted completed => handler.HandleAsync(completed, TestContext.Current.CancellationToken),
            TranscriptionFailed failed => handler.HandleAsync(failed, TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        });
    }

    private async Task<TranscriptionJob> AddJobAsync(bool callback)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(_storage, $"{id}.wav");
        await File.WriteAllBytesAsync(path, [0], TestContext.Current.CancellationToken);
        var job = new TranscriptionJob
        {
            Id = id,
            Status = JobStatus.Queued,
            Language = "pl",
            AudioPath = path,
            AudioDuration = TimeSpan.FromSeconds(1),
            CallbackUrl = callback ? new Uri("https://client.example/hook") : null,
            CreatedAt = _time.GetUtcNow(),
        };
        await using var context = Context();
        context.Jobs.Add(job);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return job;
    }

    private async Task<TranscriptionJob?> FindAsync(Guid id)
    {
        await using var context = Context();
        return await context.Jobs.FindAsync([id], TestContext.Current.CancellationToken);
    }

    private async Task<List<Guid>> DrainCallbacksAsync()
    {
        var drained = new List<Guid>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await foreach (var id in _callbacks.ReadAllAsync(timeout.Token))
            {
                drained.Add(id);
            }
        }
        catch (OperationCanceledException)
        {
        }

        return drained;
    }

    private AudioStorage Storage() => new(Microsoft.Extensions.Options.Options.Create(new ApiOptions { StoragePath = _storage }));

    private TranscriptionDbContext Context() =>
        new(new DbContextOptionsBuilder<TranscriptionDbContext>().UseSqlite(_connection).Options);
}
