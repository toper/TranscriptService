using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TranscriptService.Api.Jobs;

/// <summary>Lets <c>dotnet ef</c> create migrations without starting the API and its dependencies.</summary>
internal sealed class TranscriptionDbContextFactory : IDesignTimeDbContextFactory<TranscriptionDbContext>
{
    public TranscriptionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TranscriptionDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
