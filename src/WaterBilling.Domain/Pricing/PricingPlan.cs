using WaterBilling.Domain.Common;
using WaterBilling.Domain.Meters;

namespace WaterBilling.Domain.Pricing;

/// <summary>
/// A named tariff that meters are assigned to. The plan itself carries no rates —
/// rates live on immutable <see cref="PricingPlanVersion"/> rows, so changing a
/// tariff can never alter what an already-issued invoice says (ADR-0004).
/// </summary>
public sealed class PricingPlan : Entity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>ISO 4217. One currency per plan; the platform does not convert.</summary>
    public string Currency { get; set; } = "INR";

    public bool IsDefault { get; set; }

    public ICollection<PricingPlanVersion> Versions { get; set; } = [];

    public ICollection<Meter> Meters { get; set; } = [];
}
