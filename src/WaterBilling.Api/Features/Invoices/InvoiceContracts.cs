namespace WaterBilling.Api.Features.Invoices;

/// <summary>
/// Defaults to the previous calendar month, which is what the assignment asks for.
/// An explicit period is accepted for re-runs and back-fills.
/// </summary>
public sealed record GenerateInvoicesRequest(
    bool DryRun = false,
    bool IssueImmediately = true,
    DateTimeOffset? PeriodStartUtc = null,
    DateTimeOffset? PeriodEndUtc = null);

public sealed record InvoiceLineResponse(
    int SortOrder,
    string Kind,
    string Description,
    decimal? BandFromM3,
    decimal? BandToM3,
    decimal UnitsM3,
    decimal RatePerM3,
    decimal Amount);

public sealed record InvoiceResponse(
    Guid Id,
    string InvoiceNumber,
    Guid MeterId,
    string MeterSerial,
    Guid? CustomerId,
    string? CustomerName,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    decimal? OpeningTotalM3,
    decimal? ClosingTotalM3,
    DateTimeOffset? OpeningReadingAtUtc,
    DateTimeOffset? ClosingReadingAtUtc,
    decimal ConsumptionM3,
    int ReadingCount,
    IReadOnlyList<string> ConsumptionFlags,
    string PricingPlanName,
    int PricingPlanVersionNumber,
    decimal FixedCharge,
    decimal UsageCharge,
    decimal TaxRatePercent,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal AmountPaid,
    decimal AmountDue,
    string Currency,
    string Status,
    DateTimeOffset? IssuedAtUtc,
    DateTimeOffset? DueAtUtc,
    DateTimeOffset? PaidAtUtc,
    IReadOnlyList<InvoiceLineResponse> Lines);

public sealed record InvoicePageResponse(int Page, int PageSize, int TotalCount, IReadOnlyList<InvoiceResponse> Invoices);

public sealed record BillingRunItemResponse(
    Guid MeterId,
    string MeterSerial,
    string Outcome,
    Guid? InvoiceId,
    string? InvoiceNumber,
    decimal? ConsumptionM3,
    decimal? TotalAmount,
    IReadOnlyList<string> ConsumptionFlags,
    string? Message);

/// <summary>
/// The answer to "did billing work this month?" — per meter, with a reason for every
/// meter that was not invoiced (ADR-0007).
/// </summary>
public sealed record BillingRunResponse(
    Guid Id,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    string Status,
    bool IsDryRun,
    int MetersConsidered,
    int InvoicesGenerated,
    int Skipped,
    int Failed,
    decimal TotalBilledAmount,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<BillingRunItemResponse> Items);

public sealed record RecordPaymentRequest(
    decimal Amount,
    string Provider = "mock",
    string? ProviderReference = null,
    string? IdempotencyKey = null);

public sealed record PaymentResponse(
    Guid Id,
    Guid InvoiceId,
    decimal Amount,
    string Currency,
    string Status,
    string Provider,
    string? ProviderReference,
    DateTimeOffset InitiatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureReason,
    decimal InvoiceAmountDue,
    string InvoiceStatus);
