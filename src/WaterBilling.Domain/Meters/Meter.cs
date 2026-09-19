using WaterBilling.Domain.Common;
using WaterBilling.Domain.Pricing;
using WaterBilling.Domain.Readings;
using WaterBilling.Domain.Users;

namespace WaterBilling.Domain.Meters;

public sealed class Meter : Entity
{
    /// <summary>Manufacturer serial, unique across the estate. The human-facing identifier.</summary>
    public required string SerialNumber { get; set; }

    public string? Model { get; set; }

    public string? LocationDescription { get; set; }

    public DateTimeOffset InstalledAtUtc { get; set; }

    public MeterStatus Status { get; set; } = MeterStatus.Provisioned;

    /// <summary>
    /// Owning customer. Nullable: a meter can be commissioned and ingesting before
    /// it is assigned to an account (new build, vacant premises). Unassigned meters
    /// are billed to no one and are reported in the billing run as skipped.
    /// </summary>
    public Guid? CustomerId { get; set; }

    public User? Customer { get; set; }

    /// <summary>
    /// Pricing plan family this meter is billed on. The specific immutable
    /// <see cref="PricingPlanVersion"/> applied is resolved per billing period.
    /// </summary>
    public Guid? PricingPlanId { get; set; }

    public PricingPlan? PricingPlan { get; set; }

    // --- Supply control (relay valve) ---
    public SupplyState DesiredSupplyState { get; set; } = SupplyState.Open;

    public SupplyState? ReportedSupplyState { get; set; }

    public DateTimeOffset? SupplyStateChangedAtUtc { get; set; }

    public string? SupplyStateReason { get; set; }

    public ICollection<MeterReading> Readings { get; set; } = [];

    public ICollection<MeterApiKey> ApiKeys { get; set; } = [];
}
