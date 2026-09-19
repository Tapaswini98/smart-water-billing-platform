namespace WaterBilling.Domain.Readings;

/// <summary>
/// Why a reading was flagged. Flags, not a single value: one reading can be both
/// a reset and implausible against its reported flow.
/// </summary>
[Flags]
public enum ReadingAnomaly
{
    None = 0,

    /// <summary>TOTAL went backwards — meter reset, replacement or rollover (ADR-0003).</summary>
    TotalDecreased = 1 << 0,

    /// <summary>Change in TOTAL disagrees materially with FLOW x elapsed hours.</summary>
    FlowTotalMismatch = 1 << 1,

    /// <summary>Consumption over the interval exceeds the meter's plausible maximum.</summary>
    ImplausibleSpike = 1 << 2,

    /// <summary>Non-zero FLOW sustained with no zero-flow window — candidate leak.</summary>
    ContinuousFlow = 1 << 3,

    /// <summary>Arrived out of order relative to already-stored readings. Accepted, not an error.</summary>
    OutOfOrderArrival = 1 << 4
}
