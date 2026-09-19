using Microsoft.EntityFrameworkCore;
using WaterBilling.Domain.Readings;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Ingestion;

/// <summary>
/// Stores meter readings and classifies them. The rules below are decided, not
/// merely noticed (ADR-0003); each one has a stated reason because each one changes
/// what a customer is billed.
/// </summary>
public sealed class IngestionService(
    WaterBillingDbContext db,
    TimeProvider timeProvider,
    ILogger<IngestionService> logger)
{
    /// <summary>
    /// How far ahead of server time a reading may be timestamped before it is
    /// rejected. Device clocks drift and NTP sync is not guaranteed on an isolated
    /// LAN; a few minutes of tolerance avoids discarding good data, while anything
    /// beyond it would let a faulty clock bill a future period.
    /// </summary>
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Readings older than this are rejected: a device replaying a months-old buffer
    /// would silently alter invoices that have already been issued. Such data is
    /// loaded deliberately by an admin, not accepted blind from the field.
    /// </summary>
    public static readonly TimeSpan MaxBackdating = TimeSpan.FromDays(35);

    /// <summary>
    /// Relative tolerance when cross-checking dTOTAL against FLOW x elapsed hours.
    /// Generous, because FLOW is an instantaneous sample and the comparison is only
    /// meant to catch order-of-magnitude disagreement, not measurement noise.
    /// </summary>
    private const decimal FlowMismatchTolerance = 0.5m;

    public async Task<ReadingIngestionResult> IngestAsync(
        Guid meterId,
        ReadingRequest request,
        ReadingSource source,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var readingAt = request.ReadingAtUtc.ToUniversalTime();

        if (readingAt > now + ClockSkewTolerance)
        {
            return ReadingIngestionResult.Rejected(
                readingAt,
                $"Reading is timestamped {readingAt:O}, which is more than {ClockSkewTolerance.TotalMinutes:0} minutes ahead of server time ({now:O}). Check the device clock.");
        }

        if (readingAt < now - MaxBackdating)
        {
            return ReadingIngestionResult.Rejected(
                readingAt,
                $"Reading is older than the {MaxBackdating.TotalDays:0}-day backdating limit. Load historical data through the admin CSV import so that already-issued invoices are reviewed.");
        }

        // Idempotency check before insert. The unique index is still the authority —
        // this only avoids the common case reaching the database as an exception.
        var alreadyStored = await db.MeterReadings
            .AsNoTracking()
            .AnyAsync(r => r.MeterId == meterId && r.ReadingAtUtc == readingAt, cancellationToken);

        if (alreadyStored)
        {
            return ReadingIngestionResult.Duplicate(readingAt);
        }

        var neighbours = await LoadNeighboursAsync(meterId, readingAt, cancellationToken);

        var (anomalies, notes) = Classify(request, readingAt, neighbours);

        var reading = new MeterReading
        {
            MeterId = meterId,
            ReadingAtUtc = readingAt,
            ReceivedAtUtc = now,
            TotalM3 = request.TotalM3,
            FlowM3PerHour = request.FlowM3PerHour,
            Source = source,
            Anomalies = anomalies,
            AnomalyNotes = notes
        };

        db.MeterReadings.Add(reading);

        if (anomalies.HasFlag(ReadingAnomaly.TotalDecreased) && neighbours.Previous is { } previous)
        {
            // A reset is an operational event, not just a flag on a row: someone has to
            // decide whether the meter was replaced or is faulty, and billing has to
            // know to sum across the discontinuity.
            db.MeterResetEvents.Add(new MeterResetEvent
            {
                MeterId = meterId,
                DetectedAtUtc = readingAt,
                PreviousTotalM3 = previous.TotalM3,
                NewTotalM3 = request.TotalM3,
                TriggeringReadingId = reading.Id,
                Notes = notes
            });

            logger.LogWarning(
                "Meter {MeterId} TOTAL decreased from {Previous} to {Current} m3 at {ReadingAt}. Recorded as a reset event.",
                meterId, previous.TotalM3, request.TotalM3, readingAt);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two concurrent posts of the same reading. The index did its job; the
            // caller gets the same answer it would have got a millisecond earlier.
            db.ChangeTracker.Clear();
            return ReadingIngestionResult.Duplicate(readingAt);
        }

        return ReadingIngestionResult.Accepted(reading.Id, readingAt, Describe(anomalies), notes);
    }

    /// <summary>
    /// A reading's classification depends only on its immediate neighbours in time,
    /// which is what keeps ingestion O(1) per reading regardless of history size.
    /// </summary>
    private async Task<Neighbours> LoadNeighboursAsync(Guid meterId, DateTimeOffset readingAt, CancellationToken cancellationToken)
    {
        var previous = await db.MeterReadings
            .AsNoTracking()
            .Where(r => r.MeterId == meterId && r.ReadingAtUtc < readingAt)
            .OrderByDescending(r => r.ReadingAtUtc)
            .Select(r => new ReadingPoint(r.ReadingAtUtc, r.TotalM3, r.FlowM3PerHour))
            .FirstOrDefaultAsync(cancellationToken);

        var next = await db.MeterReadings
            .AsNoTracking()
            .Where(r => r.MeterId == meterId && r.ReadingAtUtc > readingAt)
            .OrderBy(r => r.ReadingAtUtc)
            .Select(r => new ReadingPoint(r.ReadingAtUtc, r.TotalM3, r.FlowM3PerHour))
            .FirstOrDefaultAsync(cancellationToken);

        return new Neighbours(
            previous == default ? null : previous,
            next == default ? null : next);
    }

    private static (ReadingAnomaly Anomalies, string? Notes) Classify(
        ReadingRequest request,
        DateTimeOffset readingAt,
        Neighbours neighbours)
    {
        var anomalies = ReadingAnomaly.None;
        var notes = new List<string>();

        if (neighbours.Next is not null)
        {
            // Accepted, not rejected: consumption is computed from the time-ordered
            // series, so the order readings ARRIVE in cannot change any invoice.
            anomalies |= ReadingAnomaly.OutOfOrderArrival;
            notes.Add("Arrived after a later-timestamped reading was already stored.");
        }

        if (neighbours.Previous is { } previous)
        {
            var delta = request.TotalM3 - previous.TotalM3;
            var elapsedHours = (decimal)(readingAt - previous.AtUtc).TotalHours;

            if (delta < 0m)
            {
                anomalies |= ReadingAnomaly.TotalDecreased;
                notes.Add($"TOTAL decreased from {previous.TotalM3} to {request.TotalM3} m3 — meter reset, replacement or register rollover.");
            }
            else if (request.FlowM3PerHour is { } flow && elapsedHours > 0m)
            {
                // FLOW never bills anything; it is a free consistency check on TOTAL,
                // and disagreement usually means a sensor fault worth investigating.
                var expected = flow * elapsedHours;
                var scale = Math.Max(Math.Max(expected, delta), 0.001m);

                if (Math.Abs(expected - delta) / scale > FlowMismatchTolerance)
                {
                    anomalies |= ReadingAnomaly.FlowTotalMismatch;
                    notes.Add($"Change in TOTAL ({delta:0.###} m3) disagrees with FLOW x elapsed time ({expected:0.###} m3) over {elapsedHours:0.##} h.");
                }
            }

            if (request.FlowM3PerHour is > 0m && previous.FlowM3PerHour is > 0m && elapsedHours >= 24m)
            {
                anomalies |= ReadingAnomaly.ContinuousFlow;
                notes.Add("Non-zero flow sustained across a 24-hour window with no idle period — possible leak.");
            }
        }

        return (anomalies, notes.Count == 0 ? null : string.Join(" ", notes));
    }

    private static IReadOnlyList<string> Describe(ReadingAnomaly anomalies) =>
        anomalies is ReadingAnomaly.None
            ? []
            : [.. Enum.GetValues<ReadingAnomaly>()
                .Where(flag => flag != ReadingAnomaly.None && anomalies.HasFlag(flag))
                .Select(flag => flag.ToString())];

    /// <summary>PostgreSQL SQLSTATE 23505: unique_violation.</summary>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private readonly record struct Neighbours(ReadingPoint? Previous, ReadingPoint? Next);
}
