using Microsoft.EntityFrameworkCore;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Tests.Support;
using TranscriptService.Contracts;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Tests.Jobs;

public sealed class PersistenceTests : IAsyncLifetime
{
    private SqliteJobStore _store = null!;

    public async ValueTask InitializeAsync() => _store = await SqliteJobStore.CreateAsync();

    [Fact]
    public async Task Save_WritesFileNamedByJobId()
    {
        var jobId = Guid.NewGuid();

        var path = await _store.Storage().SaveAsync(jobId, new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken);

        path.Should().Be(Path.Combine(_store.StoragePath, $"{jobId}.wav"));
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Delete_MissingFile_DoesNotThrow() =>
        FluentActions.Invoking(() => _store.Storage().Delete(Path.Combine(_store.StoragePath, "missing.wav"))).Should().NotThrow();

    [Fact]
    public async Task Job_WithSegmentsAndTimestamps_RoundTripsThroughSqlite()
    {
        var created = new DateTimeOffset(2026, 10, 31, 12, 0, 0, 123, TimeSpan.Zero);
        var job = new TranscriptionJob
        {
            Id = Guid.NewGuid(),
            Status = JobStatus.Completed,
            Language = "pl",
            AudioPath = "/data/audio/x.wav",
            AudioDuration = TimeSpan.FromSeconds(3.5),
            CreatedAt = created,
            FinishedAt = created.AddSeconds(2),
            CallbackUrl = new Uri("https://client.example/hooks/transcripts"),
            Segments = [new TranscriptSegment("dzień dobry", TimeSpan.Zero, TimeSpan.FromSeconds(1.2))],
        };

        await using (var context = _store.Context())
        {
            context.Jobs.Add(job);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = _store.Context())
        {
            var stored = await context.Jobs.SingleAsync(j => j.FinishedAt > created, TestContext.Current.CancellationToken);
            stored.Should().BeEquivalentTo(job);
        }
    }

    public async ValueTask DisposeAsync() => await _store.DisposeAsync();
}
