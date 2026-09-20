namespace WaterBilling.Api.Features.Meters;

public sealed record CreateMeterRequest(
    string SerialNumber,
    string? Model = null,
    string? LocationDescription = null,
    DateTimeOffset? InstalledAtUtc = null,
    Guid? CustomerId = null,
    Guid? PricingPlanId = null);

public sealed record UpdateMeterRequest(
    string Status,
    string? Model = null,
    string? LocationDescription = null,
    Guid? PricingPlanId = null);

public sealed record AssignMeterRequest(Guid? CustomerId = null);

public sealed record SupplyStateRequest(string DesiredState, string Reason);

public sealed record MeterResponse(
    Guid Id,
    string SerialNumber,
    string? Model,
    string? LocationDescription,
    DateTimeOffset InstalledAtUtc,
    string Status,
    Guid? CustomerId,
    string? CustomerName,
    Guid? PricingPlanId,
    string? PricingPlanName,
    string DesiredSupplyState,
    string? ReportedSupplyState,
    string? SupplyStateReason,
    DateTimeOffset? LastReadingAtUtc,
    decimal? LatestTotalM3);

public sealed record MeterPageResponse(int Page, int PageSize, int TotalCount, IReadOnlyList<MeterResponse> Meters);

/// <summary>
/// The plaintext key appears here and nowhere else, ever. It is not stored, cannot
/// be recovered, and is not written to the audit log — only its prefix is.
/// </summary>
public sealed record IssuedApiKeyResponse(
    Guid KeyId,
    Guid MeterId,
    string MeterSerial,
    string ApiKey,
    string Prefix,
    string HeaderName,
    DateTimeOffset? ExpiresAtUtc,
    string Warning);

public sealed record ApiKeySummaryResponse(
    Guid KeyId,
    string Prefix,
    string? Label,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    bool IsUsable);

public sealed record IssueApiKeyRequest(string? Label = null, int? ExpiresInDays = null);
