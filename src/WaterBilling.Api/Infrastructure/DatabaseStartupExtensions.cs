using Microsoft.EntityFrameworkCore;
using WaterBilling.Infrastructure.Persistence;
using WaterBilling.Infrastructure.Seeding;

namespace WaterBilling.Api.Infrastructure;

public static class DatabaseStartupExtensions
{
    /// <summary>
    /// Applies migrations and, in Development, seeds demo data.
    /// <para>
    /// Migrating on startup is a deliberate trade for a single-instance on-prem
    /// deployment: it makes <c>docker compose up</c> a one-command experience and
    /// removes a manual step that gets forgotten during a site visit. The limits are
    /// stated in ADR-0008 — with more than one API replica, or with a migration that
    /// rewrites a large table, this moves to a separate migration job in the pipeline.
    /// </para>
    /// </summary>
    public static async Task ApplyDatabaseMigrationsAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        var db = scope.ServiceProvider.GetRequiredService<WaterBillingDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();

        if (pending.Length > 0)
        {
            logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Length, string.Join(", ", pending));
            await db.Database.MigrateAsync();
        }
        else
        {
            logger.LogInformation("Database schema is up to date.");
        }

        if (app.Environment.IsDevelopment())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        }
    }
}
