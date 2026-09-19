using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WaterBilling.Domain.Billing;
using WaterBilling.Domain.Common;
using WaterBilling.Domain.Meters;
using WaterBilling.Domain.Pricing;
using WaterBilling.Domain.Readings;
using WaterBilling.Domain.Users;

namespace WaterBilling.Infrastructure.Persistence;

public sealed class WaterBillingDbContext(
    DbContextOptions<WaterBillingDbContext> options,
    TimeProvider timeProvider) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Meter> Meters => Set<Meter>();

    public DbSet<MeterApiKey> MeterApiKeys => Set<MeterApiKey>();

    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();

    public DbSet<MeterResetEvent> MeterResetEvents => Set<MeterResetEvent>();

    public DbSet<PricingPlan> PricingPlans => Set<PricingPlan>();

    public DbSet<PricingPlanVersion> PricingPlanVersions => Set<PricingPlanVersion>();

    public DbSet<PricingSlab> PricingSlabs => Set<PricingSlab>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();

    public DbSet<BillingRun> BillingRuns => Set<BillingRun>();

    public DbSet<BillingRunItem> BillingRunItems => Set<BillingRunItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WaterBillingDbContext).Assembly);
        ApplySnakeCaseNames(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Npgsql maps DateTimeOffset to `timestamptz` and refuses any value whose
        // offset is not zero. Normalising on the way in makes "always store UTC"
        // a property of the model rather than a rule every call site must remember.
        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();

        configurationBuilder
            .Properties<DateTimeOffset?>()
            .HaveConversion<UtcNullableDateTimeOffsetConverter>();

        // Volumes: 1 litre resolution on m3, headroom to 99,999,999,999.999 m3.
        configurationBuilder.Properties<decimal>().HavePrecision(18, 3);

        configurationBuilder.Properties<string>().HaveMaxLength(512);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// Created/updated timestamps are set centrally rather than by each handler:
    /// one place to be correct, and one place that uses the injected
    /// <see cref="TimeProvider"/> so tests can control the clock.
    /// </summary>
    private void StampTimestamps()
    {
        var now = timeProvider.GetUtcNow();

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = entry.Entity.CreatedAtUtc == default ? now : entry.Entity.CreatedAtUtc;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    break;
            }
        }
    }

    /// <summary>
    /// PostgreSQL folds unquoted identifiers to lower case, so PascalCase columns
    /// end up needing quotes in every hand-written query, backup script and psql
    /// session. snake_case keeps the database usable from outside the application.
    /// </summary>
    private static void ApplySnakeCaseNames(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.GetTableName() is { } tableName)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? string.Empty));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName() ?? string.Empty));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? string.Empty));
            }
        }
    }

    private static string ToSnakeCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            if (char.IsUpper(current))
            {
                var previousIsLower = i > 0 && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]));
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);

                if (i > 0 && value[i - 1] != '_' && (previousIsLower || nextIsLower))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}

internal sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(value => value.ToUniversalTime(), value => value.ToUniversalTime())
    {
    }
}

internal sealed class UtcNullableDateTimeOffsetConverter : ValueConverter<DateTimeOffset?, DateTimeOffset?>
{
    public UtcNullableDateTimeOffsetConverter()
        : base(
            value => value == null ? null : value.Value.ToUniversalTime(),
            value => value == null ? null : value.Value.ToUniversalTime())
    {
    }
}
