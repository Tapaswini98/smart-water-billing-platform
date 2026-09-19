using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WaterBilling.Domain.Billing;
using WaterBilling.Domain.Common;

namespace WaterBilling.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(64).IsRequired();
        builder.Property(p => p.ProviderReference).HasMaxLength(128);
        builder.Property(p => p.IdempotencyKey).HasMaxLength(128);
        builder.Property(p => p.FailureReason).HasMaxLength(512);

        builder.HasIndex(p => p.InvoiceId).HasDatabaseName("ix_payments_invoice_id");

        builder.HasIndex(p => new { p.Provider, p.ProviderReference })
            .IsUnique()
            .HasDatabaseName("ix_payments_provider_reference_unique")
            .HasFilter("provider_reference IS NOT NULL");

        // A retried checkout must be recognised as the same payment, not charged twice.
        builder.HasIndex(p => p.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ix_payments_idempotency_key_unique")
            .HasFilter("idempotency_key IS NOT NULL");

        builder.HasOne(p => p.Invoice)
            .WithMany(i => i.Payments)
            .HasForeignKey(p => p.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.PaidBy)
            .WithMany()
            .HasForeignKey(p => p.PaidByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("payment_events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.PayloadJson).HasColumnType("jsonb");

        builder.HasIndex(e => new { e.PaymentId, e.OccurredAtUtc })
            .HasDatabaseName("ix_payment_events_payment_id_occurred_at");

        builder.HasOne(e => e.Payment)
            .WithMany(p => p.Events)
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.EntityType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.ActorEmail).HasMaxLength(256);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.ChangesJson).HasColumnType("jsonb");

        builder.HasIndex(a => a.OccurredAtUtc)
            .HasDatabaseName("ix_audit_log_occurred_at")
            .IsDescending();

        builder.HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("ix_audit_log_entity");
    }
}
