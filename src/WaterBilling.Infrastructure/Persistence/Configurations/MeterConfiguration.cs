using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WaterBilling.Domain.Meters;

namespace WaterBilling.Infrastructure.Persistence.Configurations;

internal sealed class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> builder)
    {
        builder.ToTable("meters");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.SerialNumber).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Model).HasMaxLength(128);
        builder.Property(m => m.LocationDescription).HasMaxLength(512);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(m => m.DesiredSupplyState).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.ReportedSupplyState).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.SupplyStateReason).HasMaxLength(512);

        builder.HasIndex(m => m.SerialNumber)
            .IsUnique()
            .HasDatabaseName("ix_meters_serial_number_unique")
            .HasFilter("deleted_at_utc IS NULL");

        builder.HasIndex(m => m.CustomerId).HasDatabaseName("ix_meters_customer_id");

        builder.HasOne(m => m.Customer)
            .WithMany(u => u.Meters)
            .HasForeignKey(m => m.CustomerId)
            // Deleting a customer must never orphan billing history. The meter is
            // retained and simply becomes unassigned.
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.PricingPlan)
            .WithMany(p => p.Meters)
            .HasForeignKey(m => m.PricingPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(m => m.DeletedAtUtc == null);
    }
}

internal sealed class MeterApiKeyConfiguration : IEntityTypeConfiguration<MeterApiKey>
{
    public void Configure(EntityTypeBuilder<MeterApiKey> builder)
    {
        builder.ToTable("meter_api_keys");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Prefix).HasMaxLength(16).IsRequired();
        builder.Property(k => k.KeyHash).HasMaxLength(128).IsRequired();
        builder.Property(k => k.Label).HasMaxLength(128);

        // Ingestion authenticates on every request, so the prefix lookup is the
        // hottest index in the system after (meter_id, reading_at_utc).
        builder.HasIndex(k => k.Prefix).HasDatabaseName("ix_meter_api_keys_prefix");
        builder.HasIndex(k => k.MeterId).HasDatabaseName("ix_meter_api_keys_meter_id");

        builder.HasOne(k => k.Meter)
            .WithMany(m => m.ApiKeys)
            .HasForeignKey(k => k.MeterId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(k => k.DeletedAtUtc == null);
    }
}
