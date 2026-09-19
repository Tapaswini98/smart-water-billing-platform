using Shouldly;
using Xunit;
using WaterBilling.Domain.Billing;
using WaterBilling.Domain.Common;
using WaterBilling.Domain.Readings;

namespace WaterBilling.Domain.Tests;

/// <summary>
/// The rules in ADR-0003, one test each. No database, no host, no fixtures —
/// which is the entire reason the calculator is a pure function.
/// </summary>
public sealed class ConsumptionCalculatorTests
{
    private static readonly DateTimeOffset PeriodStart = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset PeriodEnd = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateRange August = new(PeriodStart, PeriodEnd);

    [Fact]
    public void Consumption_is_the_difference_between_the_anchors()
    {
        var readings = new[]
        {
            Reading(PeriodStart.AddDays(-1), 1000m),
            Reading(PeriodStart.AddDays(10), 1015m),
            Reading(PeriodEnd.AddMinutes(-5), 1042.5m)
        };

        var result = ConsumptionCalculator.Calculate(readings, August);

        result.ConsumptionM3.ShouldBe(42.5m);
        result.OpeningTotalM3.ShouldBe(1000m);
        result.ClosingTotalM3.ShouldBe(1042.5m);
        result.Flags.ShouldBe(ConsumptionFlags.None);
    }

    [Fact]
    public void Consecutive_periods_tile_without_gap_or_overlap()
    {
        // The property that matters most: billing August then September must total
        // exactly the same as billing the two months as one span.
        var readings = new[]
        {
            Reading(PeriodStart.AddDays(-1), 100m),
            Reading(PeriodStart.AddDays(15), 130m),
            Reading(PeriodEnd.AddMinutes(-1), 160m),
            Reading(PeriodEnd.AddDays(15), 190m),
            Reading(new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero), 205m)
        };

        var september = new DateRange(PeriodEnd, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        var august = ConsumptionCalculator.Calculate(readings, August);
        var sept = ConsumptionCalculator.Calculate(readings, september);
        var combined = ConsumptionCalculator.Calculate(readings, new DateRange(PeriodStart, september.End));

        (august.ConsumptionM3 + sept.ConsumptionM3).ShouldBe(combined.ConsumptionM3);
    }

    [Fact]
    public void Readings_after_the_period_end_do_not_affect_the_period()
    {
        var readings = new[]
        {
            Reading(PeriodStart, 500m),
            Reading(PeriodEnd.AddMinutes(-1), 520m),
            Reading(PeriodEnd.AddDays(3), 900m)
        };

        ConsumptionCalculator.Calculate(readings, August).ConsumptionM3.ShouldBe(20m);
    }

    [Fact]
    public void Arrival_order_does_not_change_the_answer()
    {
        var inOrder = new[]
        {
            Reading(PeriodStart, 10m),
            Reading(PeriodStart.AddDays(5), 20m),
            Reading(PeriodStart.AddDays(10), 35m)
        };

        var shuffled = new[] { inOrder[2], inOrder[0], inOrder[1] };

        ConsumptionCalculator.Calculate(shuffled, August).ConsumptionM3
            .ShouldBe(ConsumptionCalculator.Calculate(inOrder, August).ConsumptionM3);
    }

    [Fact]
    public void A_meter_reset_is_summed_across_rather_than_billed_as_negative()
    {
        var readings = new[]
        {
            Reading(PeriodStart, 990m),
            Reading(PeriodStart.AddDays(10), 1000m),
            // Register replaced: restarts near zero.
            Reading(PeriodStart.AddDays(11), 3m),
            Reading(PeriodEnd.AddMinutes(-1), 12m)
        };

        var result = ConsumptionCalculator.Calculate(readings, August);

        // 10 before the reset, then the 12 the new register has accumulated.
        result.ConsumptionM3.ShouldBe(22m);
        result.ConsumptionM3.ShouldBeGreaterThan(0m);
        result.ResetCount.ShouldBe(1);
        result.Flags.HasFlag(ConsumptionFlags.MeterResetDuringPeriod).ShouldBeTrue();
    }

    [Fact]
    public void No_readings_at_all_yields_zero_flagged_as_no_data()
    {
        var result = ConsumptionCalculator.Calculate([], August);

        result.ConsumptionM3.ShouldBe(0m);
        result.Flags.ShouldBe(ConsumptionFlags.NoData);
        result.OpeningTotalM3.ShouldBeNull();
    }

    [Fact]
    public void A_meter_commissioned_mid_period_is_flagged_partial_not_billed_from_zero()
    {
        var readings = new[]
        {
            Reading(PeriodStart.AddDays(12), 4m),
            Reading(PeriodEnd.AddMinutes(-1), 11m)
        };

        var result = ConsumptionCalculator.Calculate(readings, August);

        // 7, not 11: the register's initial 4 m3 predates the billing relationship.
        result.ConsumptionM3.ShouldBe(7m);
        result.Flags.HasFlag(ConsumptionFlags.PartialPeriod).ShouldBeTrue();
    }

    [Fact]
    public void A_single_reading_gives_zero_and_says_why()
    {
        var result = ConsumptionCalculator.Calculate([Reading(PeriodStart.AddDays(3), 77m)], August);

        result.ConsumptionM3.ShouldBe(0m);
        result.Flags.HasFlag(ConsumptionFlags.InsufficientReadings).ShouldBeTrue();
    }

    [Fact]
    public void A_meter_that_stopped_reporting_mid_period_is_flagged_stale()
    {
        var readings = new[]
        {
            Reading(PeriodStart, 100m),
            Reading(PeriodStart.AddDays(4), 118m)
        };

        var result = ConsumptionCalculator.Calculate(readings, August);

        result.ConsumptionM3.ShouldBe(18m);
        result.Flags.HasFlag(ConsumptionFlags.StaleClosingReading).ShouldBeTrue();
    }

    [Fact]
    public void A_late_arriving_reading_self_corrects_the_next_time_billing_runs()
    {
        var known = new[] { Reading(PeriodStart, 100m), Reading(PeriodEnd.AddMinutes(-1), 140m) };
        var withLateArrival = known.Append(Reading(PeriodStart.AddDays(9), 115m)).ToArray();

        // The intermediate reading does not change the total — the anchors do.
        ConsumptionCalculator.Calculate(withLateArrival, August).ConsumptionM3
            .ShouldBe(ConsumptionCalculator.Calculate(known, August).ConsumptionM3);
    }

    [Fact]
    public void An_inverted_period_is_rejected_loudly()
    {
        Should.Throw<ArgumentException>(() =>
            ConsumptionCalculator.Calculate([], new DateRange(PeriodEnd, PeriodStart)));
    }

    private static ReadingPoint Reading(DateTimeOffset at, decimal total, decimal? flow = null) =>
        new(at, total, flow);
}
