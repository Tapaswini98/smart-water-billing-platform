namespace WaterBilling.Domain.Billing;

public enum InvoiceStatus
{
    /// <summary>Generated but not yet released to the customer. Can be voided freely.</summary>
    Draft = 0,

    /// <summary>Released to the customer and payable. Never mutated after this point — corrections are credit notes.</summary>
    Issued = 1,

    Paid = 2,

    /// <summary>Past due date and unpaid. The trigger for supply cut-off review.</summary>
    Overdue = 3,

    /// <summary>Cancelled. Retained for audit; excluded from balances.</summary>
    Void = 4
}
