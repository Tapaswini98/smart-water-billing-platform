using WaterBilling.Domain.Readings;

namespace WaterBilling.Infrastructure.Seeding;

/// <summary>
/// Builds a synthetic but plausible reading series for the demo estate.
/// <para>
/// The anomalies are planted deliberately. A reviewer who runs a billing run against
/// clean data learns only that the happy path works; this dataset exercises the
/// duplicate, the out-of-order arrival, the register reset and the reporting gap, so
/// the flags on the resulting invoices are visible without anyone having to
/// construct the situation by hand.
/// </para>
/// </summary>
public static class ReadingGenerator
{
    /// <summary>Design cadence: one reading every 15 minutes.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    public sealed record Plan(
        Guid MeterId,
        decimal StartingTotalM3,
        double DailyAverageM3,
        bool PlantReset,
        bool PlantGap,
        bool PlantContinuousFlow);

    public static List<MeterReading> Generate(Plan plan, DateTimeOffset from, DateTimeOffset to, int seed)
    {
        // Seeded per meter, so `docker compose up` twice produces the same numbers and
        // a reviewer comparing two runs is not chasing phantom differences.
        var random = new Random(seed);
        var readings = new List<MeterReading>();

        var total = plan.StartingTotalM3;
        var perInterval = (decimal)(plan.DailyAverageM3 / (24 * 60 / Interval.TotalMinutes));

        // A reset two-thirds through, and a gap in the middle of the second month.
        var resetAt = plan.PlantReset ? from + (to - from) * 0.66 : DateTimeOffset.MaxValue;
        var gapStart = plan.PlantGap ? from + (to - from) * 0.40 : DateTimeOffset.MaxValue;
        var gapEnd = gapStart == DateTimeOffset.MaxValue ? gapStart : gapStart.AddHours(30);

        var hasReset = false;

        for (var at = from; at < to; at += Interval)
        {
            if (at >= gapStart && at < gapEnd)
            {
                // Simulates a meter that lost its link for 30 hours. The invoice for
                // that month should still be correct, because consumption comes from
                // the anchors either side, not from counting readings.
                continue;
            }

            if (!hasReset && at >= resetAt)
            {
                // Register replaced: the new unit starts near zero.
                total = 2.5m;
                hasReset = true;

                readings.Add(new MeterReading
                {
                    MeterId = plan.MeterId,
                    ReadingAtUtc = at,
                    ReceivedAtUtc = at,
                    TotalM3 = total,
                    FlowM3PerHour = 0.15m,
                    Source = ReadingSource.Seed,
                    Anomalies = ReadingAnomaly.TotalDecreased,
                    AnomalyNotes = "Seeded meter replacement: the register restarted near zero."
                });

                continue;
            }

            var hour = at.Hour;

            // Domestic demand: negligible overnight, peaks morning and evening. A
            // flat series would never exercise the leak detector or look believable.
            var shape = hour switch
            {
                >= 0 and < 5 => 0.05,
                >= 5 and < 9 => 2.2,
                >= 9 and < 17 => 0.8,
                >= 17 and < 22 => 1.9,
                _ => 0.3
            };

            var jitter = 0.85 + (random.NextDouble() * 0.3);
            var delta = perInterval * (decimal)(shape * jitter);

            // A leak candidate: this meter never returns to zero flow overnight.
            if (plan.PlantContinuousFlow && hour is >= 0 and < 5)
            {
                delta += perInterval * 0.6m;
            }

            total = Math.Round(total + delta, 3);

            readings.Add(new MeterReading
            {
                MeterId = plan.MeterId,
                ReadingAtUtc = at,
                ReceivedAtUtc = at,
                TotalM3 = total,
                FlowM3PerHour = Math.Round(delta * (decimal)(60 / Interval.TotalMinutes), 3),
                Source = ReadingSource.Seed
            });
        }

        return readings;
    }

    /// <summary>
    /// Marks a reading in the middle of the series as having arrived out of order —
    /// received well after a later reading was already stored. Ingestion accepts this
    /// and consumption is unaffected, which is the property worth demonstrating.
    /// </summary>
    public static void PlantOutOfOrderArrival(List<MeterReading> readings)
    {
        if (readings.Count < 100)
        {
            return;
        }

        var target = readings[readings.Count / 2];
        target.ReceivedAtUtc = target.ReadingAtUtc.AddHours(9);
        target.Anomalies |= ReadingAnomaly.OutOfOrderArrival;
        target.AnomalyNotes = "Seeded buffered upload: stored after a later-timestamped reading had already arrived.";
    }
}
