namespace WaterBilling.Domain.Billing;

/// <summary>
/// Caveats attached to a computed consumption figure. These ride onto the invoice
/// so a reviewer can see WHY a number is what it is without re-deriving it.
/// </summary>
[Flags]
public enum ConsumptionFlags
{
    None = 0,

    /// <summary>No readings at all in or before the period. Billed at the fixed charge only.</summary>
    NoData = 1 << 0,

    /// <summary>
    /// No reading at or before the period start, so the window is anchored on the first
    /// reading inside it. Consumption before that instant is unknowable, not zero.
    /// </summary>
    PartialPeriod = 1 << 1,

    /// <summary>Exactly one reading available — nothing to difference against.</summary>
    InsufficientReadings = 1 << 2,

    /// <summary>TOTAL decreased at least once; consumption was summed across the reset.</summary>
    MeterResetDuringPeriod = 1 << 3,

    /// <summary>
    /// The closing anchor is older than the period end by more than the staleness
    /// tolerance — the meter probably stopped reporting mid-period.
    /// </summary>
    StaleClosingReading = 1 << 4
}
