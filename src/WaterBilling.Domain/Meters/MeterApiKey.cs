using WaterBilling.Domain.Common;

namespace WaterBilling.Domain.Meters;

/// <summary>
/// Per-meter credential for the ingestion endpoint (ADR-0005).
/// A meter cannot hold a user JWT, so it authenticates with a long-lived key
/// presented in the <c>X-Meter-Key</c> header. Only the hash is stored; the
/// plaintext is shown exactly once, at creation.
/// </summary>
public sealed class MeterApiKey : Entity
{
    public required Guid MeterId { get; set; }

    public Meter? Meter { get; set; }

    /// <summary>
    /// First 8 characters of the key, stored in clear. Lets an operator identify
    /// which key a device is using, and lets lookup hit an index instead of
    /// hashing every row.
    /// </summary>
    public required string Prefix { get; set; }

    /// <summary>SHA-256 of the full key. Keys are high-entropy random, so a slow KDF buys nothing here.</summary>
    public required string KeyHash { get; set; }

    public string? Label { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public DateTimeOffset? LastUsedAtUtc { get; set; }

    public bool IsUsable(DateTimeOffset now) =>
        RevokedAtUtc is null && DeletedAtUtc is null && (ExpiresAtUtc is null || ExpiresAtUtc > now);
}
