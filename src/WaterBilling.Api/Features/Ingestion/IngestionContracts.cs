namespace WaterBilling.Api.Features.Ingestion;

/// <summary>A single observation posted by a meter.</summary>
/// <param name="ReadingAtUtc">
/// When the meter took the reading. Supplied by the device, not the server: a meter
/// that buffers through a network outage must still report when each reading happened.
/// </param>
/// <param name="TotalM3">Cumulative volume in m3.</param>
/// <param name="FlowM3PerHour">Instantaneous flow in m3/hr. Optional, diagnostic only.</param>
public sealed record ReadingRequest(
    DateTimeOffset ReadingAtUtc,
    decimal TotalM3,
    decimal? FlowM3PerHour);

/// <summary>Batch form, for devices that store and forward after a link outage.</summary>
public sealed record ReadingBatchRequest(IReadOnlyList<ReadingRequest> Readings);

/// <summary>
/// What happened to one submitted reading. <c>Status</c> is a stable machine token:
/// <c>accepted</c>, <c>duplicate_ignored</c>, or <c>rejected</c>.
/// </summary>
public sealed record ReadingIngestionResult
{
    public required string Status { get; init; }

    public required DateTimeOffset ReadingAtUtc { get; init; }

    public Guid? ReadingId { get; init; }

    /// <summary>Anomaly flags raised while storing, as readable tokens.</summary>
    public IReadOnlyList<string> Anomalies { get; init; } = [];

    public string? Message { get; init; }

    public static ReadingIngestionResult Accepted(Guid id, DateTimeOffset at, IReadOnlyList<string> anomalies, string? message = null) =>
        new() { Status = "accepted", ReadingId = id, ReadingAtUtc = at, Anomalies = anomalies, Message = message };

    public static ReadingIngestionResult Duplicate(DateTimeOffset at) =>
        new()
        {
            Status = "duplicate_ignored",
            ReadingAtUtc = at,
            Message = "A reading already exists for this meter at this timestamp. Ingestion is idempotent, so the retry was safe and nothing was changed."
        };

    public static ReadingIngestionResult Rejected(DateTimeOffset at, string message) =>
        new() { Status = "rejected", ReadingAtUtc = at, Message = message };
}

public sealed record ReadingBatchResponse(
    Guid MeterId,
    string MeterSerial,
    int Accepted,
    int Duplicates,
    int Rejected,
    IReadOnlyList<ReadingIngestionResult> Results);
