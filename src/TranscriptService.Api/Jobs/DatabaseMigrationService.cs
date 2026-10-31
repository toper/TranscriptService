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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
