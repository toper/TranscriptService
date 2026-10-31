using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Options;
using TranscriptService.Contracts;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Tests.Jobs;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _storage = Directory.CreateTempSubdirectory("audio-storage-").FullName;
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    [Fact]
    public async Task Save_WritesFileNamedByJobId()
    {
        var storage = Storage();
        var jobId = Guid.NewGuid();

        var path = await storage.SaveAsync(jobId, new MemoryStream([1, 2, 3]), TestContext.Current.CancellationToken);

        path.Should().Be(Path.Combine(_storage, $"{jobId}.wav"));
        (await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken)).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Delete_MissingFile_DoesNotThrow() =>
        FluentActions.Invoking(() => Storage().Delete(Path.Combine(_storage, "missing.wav"))).Should().NotThrow();

    [Fact]
    public async Task Job_WithSegmentsAndTimestamps_RoundTripsThroughSqlite()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
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

        await using (var context = Context())
        {
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            context.Jobs.Add(job);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = Context())
        {
            var stored = await context.Jobs.SingleAsync(j => j.FinishedAt > created, TestContext.Current.CancellationToken);
            stored.Should().BeEquivalentTo(job);
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
        Directory.Delete(_storage, recursive: true);
    }

    private AudioStorage Storage() =>
        new(Microsoft.Extensions.Options.Options.Create(new ApiOptions { StoragePath = _storage }));

    private TranscriptionDbContext Context() =>
        new(new DbContextOptionsBuilder<TranscriptionDbContext>().UseSqlite(_connection).Options);
}
