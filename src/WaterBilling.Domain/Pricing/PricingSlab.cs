using WaterBilling.Domain.Common;

namespace WaterBilling.Domain.Pricing;

/// <summary>
/// One band of a slab tariff, covering <c>(FromM3, ToM3]</c> — upper-inclusive.
/// A band written "0 to 10" therefore covers consumption up to and including
/// exactly 10 m3. Upper-inclusive matches how published utility tariffs read and
/// keeps progressive and whole-volume pricing agreeing at the boundaries.
/// </summary>
public sealed class PricingSlab : Entity
{
    public required Guid PricingPlanVersionId { get; set; }

    public PricingPlanVersion? PricingPlanVersion { get; set; }

    public required int SortOrder { get; set; }

    /// <summary>Exclusive lower bound. The first band starts at 0.</summary>
    public required decimal FromM3 { get; set; }

    /// <summary>Inclusive upper bound. Null on the final band, which is open-ended.</summary>
    public decimal? ToM3 { get; set; }

    public required decimal RatePerM3 { get; set; }
}
