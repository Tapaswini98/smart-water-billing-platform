using Microsoft.EntityFrameworkCore;
using WaterBilling.Infrastructure.Persistence;
using WaterBilling.Infrastructure.Seeding;

namespace WaterBilling.Api.Infrastructure;

/// <summary>
/// Applies pending migrations, and seeds demo data in Development, before the
/// application begins serving traffic.
/// <para>
/// A hosted service rather than a call in <c>Program.cs</c> for two reasons. The
/// host awaits <see cref="StartAsync"/> before opening the listener, so there is no
/// window where requests arrive against an un-migrated schema. And tooling that
/// builds the host without running it — <c>dotnet ef</c>, and the build-time OpenAPI
/// document generator that produces the contract the web client is generated from —
/// never starts hosted services, so neither needs a reachable database.
/// </para>
/// <para>
/// Migrating on startup is a deliberate trade for a single-instance on-premise
/// deployment; the limits are stated in ADR-0008.
/// </para>
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WaterBillingDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

        if (pending.Length > 0)
        {
            logger.LogInformation(
                "Applying {Count} pending migration(s): {Migrations}",
                pending.Length,
                string.Join(", ", pending));

            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation("Database schema is up to date.");
        }

        if (environment.IsDevelopment())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
