using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WaterBilling.Domain.Readings;

namespace WaterBilling.Infrastructure.Persistence.Configurations;

internal sealed class MeterReadingConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> builder)
    {
        builder.ToTable("meter_readings");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TotalM3).HasPrecision(18, 3).IsRequired();
        builder.Property(r => r.FlowM3PerHour).HasPrecision(12, 3);
        builder.Property(r => r.Source).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.Anomalies).HasConversion<int>().IsRequired();
        builder.Property(r => r.AnomalyNotes).HasMaxLength(512);

        // THE constraint that makes ingestion idempotent (ADR-0003). A meter that
        // retries after a timeout re-sends the same (meter, timestamp); the duplicate
        // is detected here and acknowledged rather than double-counted.
        builder.HasIndex(r => new { r.MeterId, r.ReadingAtUtc })
            .IsUnique()
            .HasDatabaseName("ix_meter_readings_meter_id_reading_at_utc_unique");

        // No second index on the same columns: a btree serves "the latest reading at
        // or before X" by scanning backwards at no extra cost, so the unique index
        // above is also the read path. (EF collapses two HasIndex calls on the same
        // properties into one anyway, which silently renamed the unique index the
        // first time this was written.)

        builder.HasOne(r => r.Meter)
            .WithMany(m => m.Readings)
            .HasForeignKey(r => r.MeterId)
            .OnDelete(DeleteBehavior.Restrict);

        // Deliberately NO soft-delete filter: readings are the immutable ledger that
        // every invoice is reproducible from. They are never deleted.
    }
}

internal sealed class MeterResetEventConfiguration : IEntityTypeConfiguration<MeterResetEvent>
{
    public void Configure(EntityTypeBuilder<MeterResetEvent> builder)
    {
        builder.ToTable("meter_reset_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.PreviousTotalM3).HasPrecision(18, 3).IsRequired();
        builder.Property(e => e.NewTotalM3).HasPrecision(18, 3).IsRequired();
        builder.Property(e => e.Notes).HasMaxLength(512);

        builder.HasIndex(e => new { e.MeterId, e.DetectedAtUtc })
            .HasDatabaseName("ix_meter_reset_events_meter_id_detected_at_utc");

        builder.HasOne(e => e.Meter)
            .WithMany()
            .HasForeignKey(e => e.MeterId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
