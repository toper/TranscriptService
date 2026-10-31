using Microsoft.EntityFrameworkCore;

namespace TranscriptService.Api.Jobs;

/// <summary>Applies pending migrations before the API serves requests.</summary>
internal sealed class DatabaseMigrationService(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TranscriptionDbContext>();
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        // Write-ahead logging lets readers (status polls) proceed while the dispatcher,
        // result handler and retention write. The mode is stored in the database file.
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
