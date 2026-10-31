using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TranscriptService.Api.Callbacks;
using TranscriptService.Api.Messaging;
using TranscriptService.Api.Tests.Support;
using TranscriptService.Contracts;
using TranscriptService.Contracts.Http;
using TranscriptService.Contracts.Messages;

namespace TranscriptService.Api.Tests.Messaging;

public sealed class TranscriptionResultHandlerTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly PendingCallbacks _callbacks = new();
    private SqliteJobStore _store = null!;

    public async ValueTask InitializeAsync() => _store = await SqliteJobStore.CreateAsync();

    [Fact]
    public async Task OnCompleted_UpdatesJobDeletesRecording_AndEnqueuesCallback()
    {
        var job = await _store.AddJobAsync(JobStatus.Queued, _time.GetUtcNow(), callback: true);

        await HandleAsync(Completed(job.Id));

        var stored = await _store.FindAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Completed);
        stored.FullText.Should().Be("dzień dobry");
        stored.Engine.Should().Be("whispernet");
        stored.Segments.Should().ContainSingle();
        stored.FinishedAt.Should().Be(_time.GetUtcNow());
        File.Exists(job.AudioPath).Should().BeFalse("the recording is not needed once transcribed");
        DrainCallbacks().Should().Equal(job.Id);
    }

    [Fact]
    public async Task OnCompleted_NoCallbackUrl_DoesNotEnqueue()
    {
        var job = await _store.AddJobAsync(JobStatus.Queued, _time.GetUtcNow());

        await HandleAsync(Completed(job.Id));

        DrainCallbacks().Should().BeEmpty();
    }

    [Fact]
    public async Task OnFailed_StoresReason_DeletesRecording_AndEnqueuesCallback()
    {
        var job = await _store.AddJobAsync(JobStatus.Queued, _time.GetUtcNow(), callback: true);

        await HandleAsync(new TranscriptionFailed(job.Id, "vosk", "Unsupported language."));

        var stored = await _store.FindAsync(job.Id);
        stored!.Status.Should().Be(JobStatus.Failed);
        stored.FailureReason.Should().Be("Unsupported language.");
        stored.Engine.Should().Be("vosk");
        File.Exists(job.AudioPath).Should().BeFalse();
        DrainCallbacks().Should().Equal(job.Id);
    }

    [Fact]
    public async Task OnCompleted_UnknownJob_IsIgnored()
    {
        await FluentActions.Awaiting(() => HandleAsync(Completed(Guid.NewGuid()))).Should().NotThrowAsync();

        DrainCallbacks().Should().BeEmpty();
    }

    [Fact]
    public async Task OnCompleted_Duplicate_DoesNotEnqueueSecondCallback()
    {
        var job = await _store.AddJobAsync(JobStatus.Queued, _time.GetUtcNow(), callback: true);

        await HandleAsync(Completed(job.Id));
        await HandleAsync(Completed(job.Id) with { FullText = "redelivered" });

        (await _store.FindAsync(job.Id))!.FullText.Should().Be("dzień dobry");
        DrainCallbacks().Should().Equal(job.Id);
    }

    public async ValueTask DisposeAsync() => await _store.DisposeAsync();

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
        await using var context = _store.Context();
        var handler = new TranscriptionResultHandler(context, _store.Storage(), _callbacks, _time, NullLogger<TranscriptionResultHandler>.Instance);
        await (message switch
        {
            TranscriptionCompleted completed => handler.HandleAsync(completed, TestContext.Current.CancellationToken),
            TranscriptionFailed failed => handler.HandleAsync(failed, TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(message)),
        });
    }

    private List<Guid> DrainCallbacks()
    {
        var drained = new List<Guid>();
        while (_callbacks.TryDequeue(out var jobId))
        {
            drained.Add(jobId);
        }

        return drained;
    }
}
