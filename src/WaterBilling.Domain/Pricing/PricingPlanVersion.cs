using WaterBilling.Domain.Common;

namespace WaterBilling.Domain.Pricing;

/// <summary>
/// An immutable snapshot of a tariff's rates, valid over [EffectiveFromUtc, EffectiveToUtc).
/// "Editing" a plan closes the current version and inserts a new one; nothing is
/// ever updated in place, so any historical invoice can be recomputed exactly.
/// </summary>
public sealed class PricingPlanVersion : Entity
{
    public required Guid PricingPlanId { get; set; }

    public PricingPlan? PricingPlan { get; set; }

    /// <summary>1-based, monotonic within the plan. What the invoice cites.</summary>
    public required int VersionNumber { get; set; }

    public PricingMode Mode { get; set; } = PricingMode.Slab;

    public SlabMode SlabMode { get; set; } = SlabMode.Progressive;

    /// <summary>Standing charge applied per invoice regardless of consumption, including zero-usage periods.</summary>
    public decimal FixedCharge { get; set; }

    /// <summary>Rate per m3 for <see cref="PricingMode.FlatRate"/>. Ignored for slab plans.</summary>
    public decimal RatePerM3 { get; set; }

    /// <summary>Percentage applied to (fixed + usage). 0 disables tax lines.</summary>
    public decimal TaxRatePercent { get; set; }

    public required DateTimeOffset EffectiveFromUtc { get; set; }

    /// <summary>Null means "current". Set when a successor version is published.</summary>
    public DateTimeOffset? EffectiveToUtc { get; set; }

    public ICollection<PricingSlab> Slabs { get; set; } = [];

    public bool IsEffectiveAt(DateTimeOffset instant) =>
        EffectiveFromUtc <= instant && (EffectiveToUtc is null || instant < EffectiveToUtc);
}
