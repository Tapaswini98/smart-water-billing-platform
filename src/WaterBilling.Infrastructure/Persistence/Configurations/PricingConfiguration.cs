using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WaterBilling.Domain.Pricing;

namespace WaterBilling.Infrastructure.Persistence.Configurations;

internal sealed class PricingPlanConfiguration : IEntityTypeConfiguration<PricingPlan>
{
    public void Configure(EntityTypeBuilder<PricingPlan> builder)
    {
        builder.ToTable("pricing_plans");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(1024);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();

        builder.HasIndex(p => p.Name)
            .IsUnique()
            .HasDatabaseName("ix_pricing_plans_name_unique")
            .HasFilter("deleted_at_utc IS NULL");

        // At most one default plan. Enforced in the database, because "we check it in
        // the service layer" stops being true the first time two admins click save.
        builder.HasIndex(p => p.IsDefault)
            .IsUnique()
            .HasDatabaseName("ix_pricing_plans_single_default")
            .HasFilter("is_default = true AND deleted_at_utc IS NULL");

        builder.HasQueryFilter(p => p.DeletedAtUtc == null);
    }
}

internal sealed class PricingPlanVersionConfiguration : IEntityTypeConfiguration<PricingPlanVersion>
{
    public void Configure(EntityTypeBuilder<PricingPlanVersion> builder)
    {
        builder.ToTable("pricing_plan_versions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(v => v.SlabMode).HasConversion<string>().HasMaxLength(48).IsRequired();
        builder.Property(v => v.FixedCharge).HasPrecision(18, 2);
        builder.Property(v => v.RatePerM3).HasPrecision(18, 4);
        builder.Property(v => v.TaxRatePercent).HasPrecision(6, 3);

        builder.HasIndex(v => new { v.PricingPlanId, v.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ix_pricing_plan_versions_plan_id_version_unique");

        builder.HasIndex(v => new { v.PricingPlanId, v.EffectiveFromUtc })
            .HasDatabaseName("ix_pricing_plan_versions_plan_id_effective_from");

        builder.HasOne(v => v.PricingPlan)
            .WithMany(p => p.Versions)
            .HasForeignKey(v => v.PricingPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // No soft-delete filter: a version an invoice cites must remain resolvable
        // for the life of that invoice.
    }
}

internal sealed class PricingSlabConfiguration : IEntityTypeConfiguration<PricingSlab>
{
    public void Configure(EntityTypeBuilder<PricingSlab> builder)
    {
        builder.ToTable("pricing_slabs");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.FromM3).HasPrecision(18, 3).IsRequired();
        builder.Property(s => s.ToM3).HasPrecision(18, 3);
        builder.Property(s => s.RatePerM3).HasPrecision(18, 4).IsRequired();

        builder.HasIndex(s => new { s.PricingPlanVersionId, s.SortOrder })
            .IsUnique()
            .HasDatabaseName("ix_pricing_slabs_version_id_sort_order_unique");

        builder.HasOne(s => s.PricingPlanVersion)
            .WithMany(v => v.Slabs)
            .HasForeignKey(s => s.PricingPlanVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
