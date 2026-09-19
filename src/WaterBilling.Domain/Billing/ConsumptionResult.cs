using WaterBilling.Domain.Common;

namespace WaterBilling.Domain.Billing;

/// <summary>Output of <see cref="ConsumptionCalculator"/>: the billable volume plus the evidence for it.</summary>
public sealed record ConsumptionResult
{
    public required DateRange Period { get; init; }

    /// <summary>Billable volume in m3. Never negative.</summary>
    public required decimal ConsumptionM3 { get; init; }

    /// <summary>TOTAL at the opening anchor, or null when there is no usable anchor.</summary>
    public decimal? OpeningTotalM3 { get; init; }

    public decimal? ClosingTotalM3 { get; init; }

    public DateTimeOffset? OpeningReadingAtUtc { get; init; }

    public DateTimeOffset? ClosingReadingAtUtc { get; init; }

    /// <summary>Readings considered, including the opening anchor from before the period.</summary>
    public int ReadingCount { get; init; }

    public int ResetCount { get; init; }

    public ConsumptionFlags Flags { get; init; } = ConsumptionFlags.None;

    public bool IsBillableWithConfidence => Flags is ConsumptionFlags.None;

    public static ConsumptionResult Empty(DateRange period) => new()
    {
        Period = period,
        ConsumptionM3 = 0m,
        ReadingCount = 0,
        Flags = ConsumptionFlags.NoData
    };
}
