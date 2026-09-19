using WaterBilling.Domain.Common;
using WaterBilling.Domain.Meters;

namespace WaterBilling.Domain.Billing;

/// <summary>Per-meter outcome of a billing run. This is the audit trail for "why was this meter not billed?".</summary>
public sealed class BillingRunItem : Entity
{
    public required Guid BillingRunId { get; set; }

    public BillingRun? BillingRun { get; set; }

    public required Guid MeterId { get; set; }

    public Meter? Meter { get; set; }

    public required BillingOutcome Outcome { get; set; }

    public Guid? InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public decimal? ConsumptionM3 { get; set; }

    public decimal? TotalAmount { get; set; }

    public ConsumptionFlags ConsumptionFlags { get; set; } = ConsumptionFlags.None;

    /// <summary>Operator-readable explanation. Always populated for Skipped and Failed.</summary>
    public string? Message { get; set; }
}

public enum BillingOutcome
{
    Generated = 1,

    /// <summary>
    /// An invoice for this meter and period already existed. Reported, not an error —
    /// this is what makes re-running a billing run safe.
    /// </summary>
    AlreadyBilled = 2,

    /// <summary>
    /// Billed at the fixed charge with zero consumption because the meter reported
    /// nothing in the period. Deliberately NOT skipped: a silent gap in the invoice
    /// series is how revenue goes missing (ADR-0007).
    /// </summary>
    GeneratedWithoutData = 3,

    /// <summary>Not billable: no customer assigned, no pricing plan, or not yet commissioned.</summary>
    Skipped = 4,

    Failed = 5
}
