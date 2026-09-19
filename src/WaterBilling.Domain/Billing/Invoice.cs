using WaterBilling.Domain.Common;
using WaterBilling.Domain.Meters;
using WaterBilling.Domain.Pricing;
using WaterBilling.Domain.Users;

namespace WaterBilling.Domain.Billing;

/// <summary>
/// A bill for one meter over one period. The invoice stores BOTH the tariff version
/// it was priced against AND the resulting line items in full, so it is readable
/// without joining to a tariff that may since have been superseded (ADR-0004).
/// </summary>
public sealed class Invoice : Entity
{
    /// <summary>Human-facing reference, e.g. <c>INV-2026-08-000117</c>. Unique.</summary>
    public required string InvoiceNumber { get; set; }

    public required Guid MeterId { get; set; }

    public Meter? Meter { get; set; }

    /// <summary>
    /// Snapshotted at generation. The invoice stays with the customer who held the
    /// meter during the period, even if the meter is later reassigned.
    /// </summary>
    public Guid? CustomerId { get; set; }

    public User? Customer { get; set; }

    public required DateTimeOffset PeriodStartUtc { get; set; }

    public required DateTimeOffset PeriodEndUtc { get; set; }

    // --- Consumption evidence ---
    public decimal? OpeningTotalM3 { get; set; }

    public decimal? ClosingTotalM3 { get; set; }

    public DateTimeOffset? OpeningReadingAtUtc { get; set; }

    public DateTimeOffset? ClosingReadingAtUtc { get; set; }

    public required decimal ConsumptionM3 { get; set; }

    public int ReadingCount { get; set; }

    public ConsumptionFlags ConsumptionFlags { get; set; } = ConsumptionFlags.None;

    // --- Pricing, denormalised ---
    public Guid? PricingPlanId { get; set; }

    public Guid? PricingPlanVersionId { get; set; }

    public PricingPlanVersion? PricingPlanVersion { get; set; }

    public int PricingPlanVersionNumber { get; set; }

    public required string PricingPlanName { get; set; }

    public decimal FixedCharge { get; set; }

    public decimal UsageCharge { get; set; }

    public decimal TaxRatePercent { get; set; }

    public decimal TaxAmount { get; set; }

    public required decimal TotalAmount { get; set; }

    public decimal AmountPaid { get; set; }

    public decimal AmountDue => TotalAmount - AmountPaid;

    public string Currency { get; set; } = "INR";

    // --- Lifecycle ---
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateTimeOffset? IssuedAtUtc { get; set; }

    public DateTimeOffset? DueAtUtc { get; set; }

    public DateTimeOffset? PaidAtUtc { get; set; }

    public Guid? BillingRunId { get; set; }

    public BillingRun? BillingRun { get; set; }

    public ICollection<InvoiceLineItem> LineItems { get; set; } = [];

    public ICollection<Payment> Payments { get; set; } = [];
}
