using WaterBilling.Domain.Common;
using WaterBilling.Domain.Meters;

namespace WaterBilling.Domain.Readings;

/// <summary>
/// Recorded when TOTAL decreases. Billing needs to know a reset happened so it can
/// sum across the discontinuity instead of emitting a negative invoice, and an
/// operator needs to know so the physical cause (replacement vs fault) is resolved.
/// </summary>
public sealed class MeterResetEvent : Entity
{
    public required Guid MeterId { get; set; }

    public Meter? Meter { get; set; }

    public required DateTimeOffset DetectedAtUtc { get; set; }

    /// <summary>Last TOTAL observed before the discontinuity.</summary>
    public required decimal PreviousTotalM3 { get; set; }

    /// <summary>First TOTAL observed after it.</summary>
    public required decimal NewTotalM3 { get; set; }

    /// <summary>The reading that triggered detection.</summary>
    public Guid? TriggeringReadingId { get; set; }

    public string? Notes { get; set; }

    public bool IsAcknowledged { get; set; }

    public Guid? AcknowledgedByUserId { get; set; }
}
