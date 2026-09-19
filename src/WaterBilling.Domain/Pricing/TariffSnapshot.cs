namespace WaterBilling.Domain.Pricing;

/// <summary>
/// The plain-data view of a <see cref="PricingPlanVersion"/> that the pricing engine
/// consumes. Decoupling the engine from the entities keeps it free of EF, of lazy
/// loading, and of any chance that a navigation property changes the answer.
/// </summary>
public sealed record TariffSnapshot
{
    public required Guid PlanId { get; init; }

    public required Guid PlanVersionId { get; init; }

    public required int VersionNumber { get; init; }

    public required string PlanName { get; init; }

    public string Currency { get; init; } = "INR";

    public PricingMode Mode { get; init; } = PricingMode.Slab;

    public SlabMode SlabMode { get; init; } = SlabMode.Progressive;

    public decimal FixedCharge { get; init; }

    public decimal RatePerM3 { get; init; }

    public decimal TaxRatePercent { get; init; }

    public IReadOnlyList<SlabSnapshot> Slabs { get; init; } = [];

    public static TariffSnapshot From(PricingPlanVersion version, string planName, string currency) => new()
    {
        PlanId = version.PricingPlanId,
        PlanVersionId = version.Id,
        VersionNumber = version.VersionNumber,
        PlanName = planName,
        Currency = currency,
        Mode = version.Mode,
        SlabMode = version.SlabMode,
        FixedCharge = version.FixedCharge,
        RatePerM3 = version.RatePerM3,
        TaxRatePercent = version.TaxRatePercent,
        Slabs = [.. version.Slabs
            .OrderBy(s => s.SortOrder)
            .Select(s => new SlabSnapshot(s.SortOrder, s.FromM3, s.ToM3, s.RatePerM3))]
    };
}

/// <summary>One band, covering <c>(FromM3, ToM3]</c>. <paramref name="ToM3"/> null means open-ended.</summary>
public readonly record struct SlabSnapshot(int SortOrder, decimal FromM3, decimal? ToM3, decimal RatePerM3);
