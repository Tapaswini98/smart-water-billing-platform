using WaterBilling.Domain.Common;
using WaterBilling.Domain.Readings;

namespace WaterBilling.Domain.Billing;

/// <summary>
/// Turns an immutable series of TOTAL readings into a billable volume for a period.
/// <para>
/// The rule (ADR-0003):
/// <c>consumption(meter, start, end) = TOTAL(latest reading at or before end) - TOTAL(latest reading at or before start)</c>,
/// walked pairwise so that a meter reset inside the period is summed across rather
/// than producing a negative figure.
/// </para>
/// <para>
/// Consequences worth stating: consecutive periods tile with no gap and no double
/// count; a late-arriving reading corrects the answer the next time it is computed,
/// with no stored counter to migrate; and the whole thing is a pure function, so
/// every rule below has a unit test and none of them need a database.
/// </para>
/// </summary>
public static class ConsumptionCalculator
{
    /// <summary>
    /// How stale the closing anchor may be before the period end without being flagged.
    /// Set from the expected reporting cadence (15 min) with generous headroom for
    /// retries and clock skew.
    /// </summary>
    public static readonly TimeSpan DefaultStalenessTolerance = TimeSpan.FromHours(6);

    /// <param name="readings">
    /// Readings for one meter. Need not be sorted, and SHOULD include the last
    /// reading before <paramref name="period"/> so the opening anchor exists.
    /// </param>
    /// <param name="period">Half-open billing window [Start, End).</param>
    /// <param name="stalenessTolerance">Overrides <see cref="DefaultStalenessTolerance"/>.</param>
    public static ConsumptionResult Calculate(
        IReadOnlyList<ReadingPoint> readings,
        DateRange period,
        TimeSpan? stalenessTolerance = null)
    {
        ArgumentNullException.ThrowIfNull(readings);

        if (period.End <= period.Start)
        {
            throw new ArgumentException($"Period end ({period.End:O}) must be after period start ({period.Start:O}).", nameof(period));
        }

        var tolerance = stalenessTolerance ?? DefaultStalenessTolerance;

        // Everything at or before the period end, oldest first. Readings after the
        // period end are irrelevant: a reading taken in October cannot bill September.
        var relevant = readings
            .Where(r => r.AtUtc <= period.End)
            .OrderBy(r => r.AtUtc)
            .ToArray();

        if (relevant.Length == 0)
        {
            return ConsumptionResult.Empty(period);
        }

        var flags = ConsumptionFlags.None;

        // Opening anchor: the last reading at or before the period start. Absent one,
        // anchor on the first reading inside the period and say so — consumption
        // before a meter's first ever reading is unknown, and claiming zero would
        // silently under-bill a meter commissioned mid-month.
        var anchorIndex = LastIndexAtOrBefore(relevant, period.Start);
        if (anchorIndex < 0)
        {
            anchorIndex = 0;
            flags |= ConsumptionFlags.PartialPeriod;
        }

        var window = relevant.AsSpan(anchorIndex);

        if (window.Length == 1)
        {
            // A single reading gives an anchor but nothing to difference against.
            return new ConsumptionResult
            {
                Period = period,
                ConsumptionM3 = 0m,
                OpeningTotalM3 = window[0].TotalM3,
                ClosingTotalM3 = window[0].TotalM3,
                OpeningReadingAtUtc = window[0].AtUtc,
                ClosingReadingAtUtc = window[0].AtUtc,
                ReadingCount = 1,
                ResetCount = 0,
                Flags = flags | ConsumptionFlags.InsufficientReadings
            };
        }

        decimal consumption = 0m;
        var resetCount = 0;

        for (var i = 1; i < window.Length; i++)
        {
            var previous = window[i - 1];
            var current = window[i];
            var delta = current.TotalM3 - previous.TotalM3;

            if (delta >= 0m)
            {
                consumption += delta;
                continue;
            }

            // TOTAL went backwards: the register was reset or the unit was replaced.
            // Water consumed between `previous` and the reset instant is unknowable
            // (the pre-reset peak was never reported), so we count only what the
            // post-reset register has accumulated. This under-bills by at most one
            // reporting interval and can never produce a negative invoice.
            consumption += current.TotalM3;
            resetCount++;
        }

        if (resetCount > 0)
        {
            flags |= ConsumptionFlags.MeterResetDuringPeriod;
        }

        var closing = window[^1];
        if (period.End - closing.AtUtc > tolerance)
        {
            flags |= ConsumptionFlags.StaleClosingReading;
        }

        return new ConsumptionResult
        {
            Period = period,
            ConsumptionM3 = consumption,
            OpeningTotalM3 = window[0].TotalM3,
            ClosingTotalM3 = closing.TotalM3,
            OpeningReadingAtUtc = window[0].AtUtc,
            ClosingReadingAtUtc = closing.AtUtc,
            ReadingCount = window.Length,
            ResetCount = resetCount,
            Flags = flags
        };
    }

    /// <summary>Binary search for the last reading at or before <paramref name="instant"/>; -1 if none.</summary>
    private static int LastIndexAtOrBefore(ReadingPoint[] ascending, DateTimeOffset instant)
    {
        var low = 0;
        var high = ascending.Length - 1;
        var found = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (ascending[mid].AtUtc <= instant)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }
}
