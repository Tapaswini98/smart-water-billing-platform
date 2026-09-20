namespace WaterBilling.Api.Features.Pricing;

public sealed record SlabRequest(decimal FromM3, decimal RatePerM3, decimal? ToM3 = null);

public sealed record CreatePricingPlanRequest(
    string Name,
    PublishVersionRequest InitialVersion,
    string? Description = null,
    string? Currency = null,
    bool IsDefault = false);

/// <summary>
/// Publishing a version never edits the current one — it closes it and opens a
/// successor, so an invoice issued last month still prices exactly as it did.
/// </summary>
public sealed record PublishVersionRequest(
    string Mode,
    decimal FixedCharge = 0m,
    decimal RatePerM3 = 0m,
    decimal TaxRatePercent = 0m,
    string? SlabMode = null,
    DateTimeOffset? EffectiveFromUtc = null,
    IReadOnlyList<SlabRequest>? Slabs = null);

public sealed record SlabResponse(int SortOrder, decimal FromM3, decimal? ToM3, decimal RatePerM3);

public sealed record PricingPlanVersionResponse(
    Guid Id,
    int VersionNumber,
    string Mode,
    string SlabMode,
    decimal FixedCharge,
    decimal RatePerM3,
    decimal TaxRatePercent,
    DateTimeOffset EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    bool IsCurrent,
    IReadOnlyList<SlabResponse> Slabs);

public sealed record PricingPlanResponse(
    Guid Id,
    string Name,
    string? Description,
    string Currency,
    bool IsDefault,
    int MeterCount,
    IReadOnlyList<PricingPlanVersionResponse> Versions);

/// <summary>Lets an admin see what a tariff would charge before publishing it.</summary>
public sealed record PriceQuoteRequest(decimal ConsumptionM3);

public sealed record PriceLineResponse(
    string Description,
    decimal? FromM3,
    decimal? ToM3,
    decimal UnitsM3,
    decimal RatePerM3,
    decimal Amount);

public sealed record PriceQuoteResponse(
    Guid PlanId,
    string PlanName,
    int PlanVersionNumber,
    string Currency,
    decimal ConsumptionM3,
    IReadOnlyList<PriceLineResponse> Lines,
    decimal FixedCharge,
    decimal UsageCharge,
    decimal Subtotal,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal Total);
