using WaterBilling.Domain.Common;
using WaterBilling.Domain.Meters;

namespace WaterBilling.Domain.Readings;

/// <summary>
/// An immutable observation from a meter. Readings are never updated or deleted:
/// the series IS the ledger, and every invoice is reproducible from it (ADR-0003).
/// </summary>
public sealed class MeterReading : Entity
{
    public required Guid MeterId { get; set; }

    public Meter? Meter { get; set; }

    /// <summary>When the meter took the reading. Part of the uniqueness key.</summary>
    public required DateTimeOffset ReadingAtUtc { get; set; }

    /// <summary>When the platform stored it. Differs from <see cref="ReadingAtUtc"/> for buffered uploads.</summary>
    public DateTimeOffset ReceivedAtUtc { get; set; }

    /// <summary>Cumulative volume in m3. Ever-increasing except across a meter reset.</summary>
    public required decimal TotalM3 { get; set; }

    /// <summary>Instantaneous flow in m3/hr. Diagnostic only — never used to compute a bill.</summary>
    public decimal? FlowM3PerHour { get; set; }

    public ReadingSource Source { get; set; } = ReadingSource.RestApi;

    public ReadingAnomaly Anomalies { get; set; } = ReadingAnomaly.None;

    public string? AnomalyNotes { get; set; }

    public bool IsAnomalous => Anomalies != ReadingAnomaly.None;
}
