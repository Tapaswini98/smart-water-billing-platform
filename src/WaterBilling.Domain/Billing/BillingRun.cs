using WaterBilling.Domain.Common;
using WaterBilling.Domain.Users;

namespace WaterBilling.Domain.Billing;

/// <summary>
/// One execution of "generate invoices for period X". Invoice generation is
/// idempotent — a unique index on (meter_id, period_start) means re-running a
/// period cannot double-bill — so the run's job is to report, per meter, what
/// happened and why (ADR-0007). A billing run that silently skips meters is worse
/// than one that fails loudly.
/// </summary>
public sealed class BillingRun : Entity
{
    public required DateTimeOffset PeriodStartUtc { get; set; }

    public required DateTimeOffset PeriodEndUtc { get; set; }

    public Guid? TriggeredByUserId { get; set; }

    public User? TriggeredBy { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public BillingRunStatus Status { get; set; } = BillingRunStatus.Running;

    public int MetersConsidered { get; set; }

    public int InvoicesGenerated { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public decimal TotalBilledAmount { get; set; }

    /// <summary>Dry runs compute and report everything but persist no invoices.</summary>
    public bool IsDryRun { get; set; }

    public string? FailureReason { get; set; }

    public ICollection<BillingRunItem> Items { get; set; } = [];
}

public enum BillingRunStatus
{
    Running = 0,
    Completed = 1,

    /// <summary>Finished, but at least one meter failed. The run is still usable; the failures are listed.</summary>
    CompletedWithErrors = 2,

    Failed = 3
}
