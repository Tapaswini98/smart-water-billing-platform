using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WaterBilling.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> construct the context without booting the API.
/// The connection string is never used to connect at design time — EF only needs a
/// provider to generate provider-specific SQL — so a placeholder is correct here.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<WaterBillingDbContext>
{
    public WaterBillingDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__WaterBilling")
            ?? "Host=localhost;Port=5432;Database=waterbilling;Username=waterbilling;Password=design-time";

        var options = new DbContextOptionsBuilder<WaterBillingDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(WaterBillingDbContext).Assembly.FullName))
            .Options;

        return new WaterBillingDbContext(options, TimeProvider.System);
    }
}
