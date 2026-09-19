using WaterBilling.Domain.Common;

namespace WaterBilling.Domain.Billing;

/// <summary>
/// One printed line of an invoice: a slab band, the flat-rate line, the standing
/// charge or the tax line. Written once at generation and never recomputed.
/// </summary>
public sealed class InvoiceLineItem : Entity
{
    public required Guid InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public required int SortOrder { get; set; }

    public required InvoiceLineKind Kind { get; set; }

    public required string Description { get; set; }

    public decimal? BandFromM3 { get; set; }

    public decimal? BandToM3 { get; set; }

    public decimal UnitsM3 { get; set; }

    public decimal RatePerM3 { get; set; }

    public required decimal Amount { get; set; }
}

public enum InvoiceLineKind
{
    /// <summary>Standing charge, independent of consumption.</summary>
    FixedCharge = 1,

    /// <summary>Volumetric charge for a band or a flat rate.</summary>
    Consumption = 2,

    Tax = 3,

    /// <summary>Manual adjustment or credit. Reserved; not emitted by the billing run.</summary>
    Adjustment = 4
}
