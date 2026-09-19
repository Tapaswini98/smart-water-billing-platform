namespace WaterBilling.Domain.Common;

/// <summary>
/// Append-only record of state-changing admin actions. Billing systems get
/// disputed; "who changed this tariff, and when" needs an answer that does not
/// depend on application logs having been retained.
/// </summary>
public sealed class AuditLogEntry : Entity
{
    public Guid? ActorUserId { get; set; }

    public string? ActorEmail { get; set; }

    /// <summary>Verb, e.g. <c>meter.created</c>, <c>pricing_plan.version_published</c>, <c>supply.cut_off</c>.</summary>
    public required string Action { get; set; }

    public required string EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public required DateTimeOffset OccurredAtUtc { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Changed fields as JSON. Never contains password hashes or API keys.</summary>
    public string? ChangesJson { get; set; }
}
