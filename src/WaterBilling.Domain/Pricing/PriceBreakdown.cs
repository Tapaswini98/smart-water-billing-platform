namespace WaterBilling.Domain.Pricing;

/// <summary>
/// The fully itemised result of pricing a volume. Every one of these lines is
/// denormalised onto the invoice, so a customer disputing a bill two years later
/// sees the exact bands and rates that produced it, not a recomputation against
/// today's tariff.
/// </summary>
public sealed record PriceBreakdown
{
    public required Guid PlanId { get; init; }

    public required Guid PlanVersionId { get; init; }

    public required int PlanVersionNumber { get; init; }

    public required string PlanName { get; init; }

    public required string Currency { get; init; }

    public required decimal ConsumptionM3 { get; init; }

    public IReadOnlyList<PriceLine> Lines { get; init; } = [];

    /// <summary>Standing charge. Applied even when consumption is zero.</summary>
    public decimal FixedCharge { get; init; }

    /// <summary>Sum of the volumetric lines.</summary>
    public decimal UsageCharge { get; init; }

    public decimal Subtotal => FixedCharge + UsageCharge;

    public decimal TaxRatePercent { get; init; }

    public decimal TaxAmount { get; init; }

    public decimal Total => Subtotal + TaxAmount;
}

/// <summary>
/// One priced band (or the single flat-rate line). <paramref name="ToM3"/> is null
/// on the open-ended final band.
/// </summary>
public readonly record struct PriceLine(
    int SortOrder,
    string Description,
    decimal? FromM3,
    decimal? ToM3,
    decimal UnitsM3,
    decimal RatePerM3,
    decimal Amount);
