using WaterBilling.Domain.Common;
using WaterBilling.Domain.Users;

namespace WaterBilling.Domain.Billing;

/// <summary>
/// A payment attempt against an invoice. Every attempt is recorded, including
/// failures — reconciliation questions are almost always about the attempts that
/// did not succeed. Provider payloads are logged separately in
/// <see cref="PaymentEvent"/> so this row stays queryable.
/// </summary>
public sealed class Payment : Entity
{
    public required Guid InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public Guid? PaidByUserId { get; set; }

    public User? PaidBy { get; set; }

    public required decimal Amount { get; set; }

    public string Currency { get; set; } = "INR";

    public PaymentStatus Status { get; set; } = PaymentStatus.Initiated;

    public required string Provider { get; set; }

    /// <summary>Provider-side identifier (order/intent id). Unique per provider.</summary>
    public string? ProviderReference { get; set; }

    /// <summary>
    /// Caller-supplied idempotency key. A retried checkout must not charge twice,
    /// and must not create a second row.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public DateTimeOffset InitiatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public string? FailureReason { get; set; }

    public ICollection<PaymentEvent> Events { get; set; } = [];
}

public enum PaymentStatus
{
    Initiated = 0,
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Refunded = 4
}

/// <summary>Append-only log of provider interactions for one payment.</summary>
public sealed class PaymentEvent : Entity
{
    public required Guid PaymentId { get; set; }

    public Payment? Payment { get; set; }

    public required string EventType { get; set; }

    public required DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>Raw provider payload, redacted of card data before storage.</summary>
    public string? PayloadJson { get; set; }
}
