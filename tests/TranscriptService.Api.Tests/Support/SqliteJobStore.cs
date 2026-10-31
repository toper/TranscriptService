using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TranscriptService.Api.Jobs;
using TranscriptService.Api.Options;
using TranscriptService.Contracts.Http;

namespace TranscriptService.Api.Tests.Support;

/// <summary>A migrated in-memory SQLite job store and a temporary recordings folder for one test.</summary>
internal sealed class SqliteJobStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    private SqliteJobStore()
    {
    }

    public string StoragePath { get; } = Directory.CreateTempSubdirectory("jobs-").FullName;

    public static async Task<SqliteJobStore> CreateAsync()
    {
        var store = new SqliteJobStore();
        await store._connection.OpenAsync();
        await using var context = store.Context();
        await context.Database.MigrateAsync();
        return store;
    }

    public TranscriptionDbContext Context() =>
        new(new DbContextOptionsBuilder<TranscriptionDbContext>().UseSqlite(_connection).Options);

    public AudioStorage Storage() =>
        new(Microsoft.Extensions.Options.Options.Create(new ApiOptions { StoragePath = StoragePath }));

    /// <summary>Stores a job and a one-byte recording for it.</summary>
    public async Task<TranscriptionJob> AddJobAsync(
        JobStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? finishedAt = null,
        bool callback = false)
    {
        var id = Guid.NewGuid();
        var job = new TranscriptionJob
        {
            Id = id,
            Status = status,
            Language = "pl",
            AudioPath = Storage().PathFor(id),
            AudioDuration = TimeSpan.FromSeconds(1),
            CallbackUrl = callback ? new Uri("https://client.example/hook") : null,
            CreatedAt = createdAt,
            FinishedAt = finishedAt,
        };
        await File.WriteAllBytesAsync(job.AudioPath, [0]);
        await using var context = Context();
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        return job;
    }

    public async Task<TranscriptionJob?> FindAsync(Guid id)
    {
        await using var context = Context();
        return await context.Jobs.FindAsync(id);
    }

    public async Task<List<Guid>> IdsAsync()
    {
        await using var context = Context();
        return await context.Jobs.Select(j => j.Id).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        Directory.Delete(StoragePath, recursive: true);
    }
}
