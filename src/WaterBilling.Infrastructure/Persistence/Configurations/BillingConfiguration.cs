using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WaterBilling.Domain.Billing;

namespace WaterBilling.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.InvoiceNumber).HasMaxLength(32).IsRequired();
        builder.Property(i => i.PricingPlanName).HasMaxLength(128).IsRequired();
        builder.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(i => i.ConsumptionFlags).HasConversion<int>().IsRequired();

        builder.Property(i => i.ConsumptionM3).HasPrecision(18, 3);
        builder.Property(i => i.OpeningTotalM3).HasPrecision(18, 3);
        builder.Property(i => i.ClosingTotalM3).HasPrecision(18, 3);
        builder.Property(i => i.FixedCharge).HasPrecision(18, 2);
        builder.Property(i => i.UsageCharge).HasPrecision(18, 2);
        builder.Property(i => i.TaxRatePercent).HasPrecision(6, 3);
        builder.Property(i => i.TaxAmount).HasPrecision(18, 2);
        builder.Property(i => i.TotalAmount).HasPrecision(18, 2);
        builder.Property(i => i.AmountPaid).HasPrecision(18, 2);

        // AmountDue is derived; it exists in C# for convenience, not in the schema.
        builder.Ignore(i => i.AmountDue);

        builder.HasIndex(i => i.InvoiceNumber)
            .IsUnique()
            .HasDatabaseName("ix_invoices_invoice_number_unique");

        // What makes invoice generation idempotent (ADR-0007): a period can be
        // re-run any number of times and a meter can still only be billed once for it.
        builder.HasIndex(i => new { i.MeterId, i.PeriodStartUtc })
            .IsUnique()
            .HasDatabaseName("ix_invoices_meter_id_period_start_unique");

        // The customer's "my invoices" screen, newest first.
        builder.HasIndex(i => new { i.CustomerId, i.PeriodStartUtc })
            .HasDatabaseName("ix_invoices_customer_id_period_start")
            .IsDescending(false, true);

        builder.HasIndex(i => i.Status).HasDatabaseName("ix_invoices_status");

        builder.HasOne(i => i.Meter)
            .WithMany()
            .HasForeignKey(i => i.MeterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Customer)
            .WithMany()
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.PricingPlanVersion)
            .WithMany()
            .HasForeignKey(i => i.PricingPlanVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.BillingRun)
            .WithMany()
            .HasForeignKey(i => i.BillingRunId)
            .OnDelete(DeleteBehavior.SetNull);

        // Issued invoices are financial records: no soft delete, no filter. A mistake
        // is corrected by voiding and re-issuing, which leaves both documents visible.
    }
}

internal sealed class InvoiceLineItemConfiguration : IEntityTypeConfiguration<InvoiceLineItem>
{
    public void Configure(EntityTypeBuilder<InvoiceLineItem> builder)
    {
        builder.ToTable("invoice_line_items");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(l => l.Description).HasMaxLength(256).IsRequired();
        builder.Property(l => l.BandFromM3).HasPrecision(18, 3);
        builder.Property(l => l.BandToM3).HasPrecision(18, 3);
        builder.Property(l => l.UnitsM3).HasPrecision(18, 3);
        builder.Property(l => l.RatePerM3).HasPrecision(18, 4);
        builder.Property(l => l.Amount).HasPrecision(18, 2).IsRequired();

        builder.HasIndex(l => new { l.InvoiceId, l.SortOrder })
            .HasDatabaseName("ix_invoice_line_items_invoice_id_sort_order");

        builder.HasOne(l => l.Invoice)
            .WithMany(i => i.LineItems)
            .HasForeignKey(l => l.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BillingRunConfiguration : IEntityTypeConfiguration<BillingRun>
{
    public void Configure(EntityTypeBuilder<BillingRun> builder)
    {
        builder.ToTable("billing_runs");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(r => r.TotalBilledAmount).HasPrecision(18, 2);
        builder.Property(r => r.FailureReason).HasMaxLength(1024);

        builder.HasIndex(r => new { r.PeriodStartUtc, r.StartedAtUtc })
            .HasDatabaseName("ix_billing_runs_period_start_started_at");

        builder.HasOne(r => r.TriggeredBy)
            .WithMany()
            .HasForeignKey(r => r.TriggeredByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BillingRunItemConfiguration : IEntityTypeConfiguration<BillingRunItem>
{
    public void Configure(EntityTypeBuilder<BillingRunItem> builder)
    {
        builder.ToTable("billing_run_items");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Outcome).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(i => i.ConsumptionFlags).HasConversion<int>().IsRequired();
        builder.Property(i => i.ConsumptionM3).HasPrecision(18, 3);
        builder.Property(i => i.TotalAmount).HasPrecision(18, 2);
        builder.Property(i => i.Message).HasMaxLength(1024);

        builder.HasIndex(i => new { i.BillingRunId, i.Outcome })
            .HasDatabaseName("ix_billing_run_items_run_id_outcome");

        builder.HasOne(i => i.BillingRun)
            .WithMany(r => r.Items)
            .HasForeignKey(i => i.BillingRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Meter)
            .WithMany()
            .HasForeignKey(i => i.MeterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Invoice)
            .WithMany()
            .HasForeignKey(i => i.InvoiceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
