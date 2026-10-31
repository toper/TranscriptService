using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TranscriptService.Contracts;

namespace TranscriptService.Api.Jobs;

/// <summary>SQLite store of transcription jobs. Jobs are short-lived: deleted after delivery or expiry.</summary>
public sealed class TranscriptionDbContext(DbContextOptions<TranscriptionDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public DbSet<TranscriptionJob> Jobs => Set<TranscriptionJob>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite cannot compare DateTimeOffset values in SQL; stored as sortable binary they can be.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<TranscriptionJob>();
        job.ToTable("Jobs");
        job.HasKey(j => j.Id);
        job.Property(j => j.Status).HasConversion<string>().HasMaxLength(16);
        job.Property(j => j.Language).HasMaxLength(8);
        job.Property(j => j.Engine).HasMaxLength(32);
        job.Property(j => j.Segments).HasConversion(
            segments => JsonSerializer.Serialize(segments, JsonOptions),
            json => JsonSerializer.Deserialize<List<TranscriptSegment>>(json, JsonOptions) ?? new List<TranscriptSegment>(),
            new ValueComparer<IReadOnlyList<TranscriptSegment>>(
                (left, right) => (left ?? Array.Empty<TranscriptSegment>()).SequenceEqual(right ?? Array.Empty<TranscriptSegment>()),
                segments => segments.Aggregate(0, (hash, segment) => HashCode.Combine(hash, segment)),
                segments => segments.ToList()));
        job.HasIndex(j => j.FinishedAt);
        job.HasIndex(j => new { j.Status, j.CreatedAt });
    }
}
