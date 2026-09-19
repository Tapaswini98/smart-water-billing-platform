using Shouldly;
using WaterBilling.Domain.Pricing;
using Xunit;

namespace WaterBilling.Domain.Tests;

/// <summary>
/// Table-driven boundary coverage for the pricing engine. The interesting values are
/// the band edges — 0, exactly at a boundary, and a hair past it — because that is
/// where an off-by-one silently overcharges every customer in a band.
/// </summary>
public sealed class PricingEngineTests
{
    /// <summary>Bands: 0-10 @ 12, 10-25 @ 22, 25-50 @ 38, 50+ @ 60. Fixed charge 100, no tax.</summary>
    private static TariffSnapshot ProgressiveSlab(decimal taxPercent = 0m) => new()
    {
        PlanId = Guid.Parse("00000000-0000-0000-0000-00000000f001"),
        PlanVersionId = Guid.Parse("00000000-0000-0000-0000-00000000f101"),
        VersionNumber = 1,
        PlanName = "Domestic Progressive Slab",
        Currency = "INR",
        Mode = PricingMode.Slab,
        SlabMode = SlabMode.Progressive,
        FixedCharge = 100m,
        TaxRatePercent = taxPercent,
        Slabs =
        [
            new SlabSnapshot(1, 0m, 10m, 12m),
            new SlabSnapshot(2, 10m, 25m, 22m),
            new SlabSnapshot(3, 25m, 50m, 38m),
            new SlabSnapshot(4, 50m, null, 60m)
        ]
    };

    /// <summary>
    /// Decimal literals cannot appear in an attribute, and letting xUnit convert a
    /// double would put a binary-floating-point value into a money calculation —
    /// exactly the bug these tests exist to catch. TheoryData keeps them decimal.
    /// </summary>
    public static TheoryData<decimal, decimal> ProgressiveCases() => new()
    {
        { 0m, 0m },              // zero usage still attracts the standing charge
        { 1m, 12m },
        { 9.999m, 119.99m },     // just below the first boundary
        { 10m, 120m },           // exactly on it: still entirely in band 1
        { 10.001m, 120.02m },    // a hair past: 10 x 12 + 0.001 x 22, rounded
        { 25m, 450m },           // 120 + 15 x 22
        { 30m, 640m },           // 120 + 330 + 5 x 38
        { 50m, 1400m },          // 120 + 330 + 25 x 38
        { 60m, 2000m }           // 120 + 330 + 950 + 10 x 60
    };

    [Theory]
    [MemberData(nameof(ProgressiveCases))]
    public void Progressive_bands_charge_only_the_units_inside_them(decimal consumption, decimal expectedUsage)
    {
        var result = PricingEngine.Price(ProgressiveSlab(), consumption);

        result.UsageCharge.ShouldBe(expectedUsage);
        result.FixedCharge.ShouldBe(100m);
        result.Total.ShouldBe(100m + expectedUsage);
    }

    [Fact]
    public void Progressive_pricing_emits_one_line_per_band_actually_used()
    {
        var result = PricingEngine.Price(ProgressiveSlab(), 30m);

        result.Lines.Count.ShouldBe(3);
        result.Lines.Select(l => l.UnitsM3).ShouldBe([10m, 15m, 5m]);
        result.Lines.Sum(l => l.UnitsM3).ShouldBe(30m);
        result.Lines.Sum(l => l.Amount).ShouldBe(result.UsageCharge);
    }

    [Fact]
    public void A_zero_consumption_invoice_still_shows_the_tariff_it_was_billed_under()
    {
        // A customer who used nothing should be able to see WHY they owe the
        // standing charge, so the band structure is still printed.
        var result = PricingEngine.Price(ProgressiveSlab(), 0m);

        result.Lines.ShouldNotBeEmpty();
        result.UsageCharge.ShouldBe(0m);
        result.Total.ShouldBe(100m);
    }

    public static TheoryData<decimal, decimal> WholeVolumeCases() => new()
    {
        { 10m, 120m },        // whole 10 at the band-1 rate
        { 10.001m, 220.02m }, // one millilitre over, and the whole volume repriced at band 2
        { 30m, 1140m }        // 30 x 38
    };

    [Theory]
    [MemberData(nameof(WholeVolumeCases))]
    public void Whole_volume_mode_charges_everything_at_the_reached_band(decimal consumption, decimal expectedUsage)
    {
        var tariff = ProgressiveSlab() with { SlabMode = SlabMode.WholeVolumeAtReachedBand };

        PricingEngine.Price(tariff, consumption).UsageCharge.ShouldBe(expectedUsage);
    }

    [Fact]
    public void Both_slab_modes_agree_on_where_a_band_boundary_sits()
    {
        // Exactly 10 m3 is in band 1 under either interpretation. If this ever fails,
        // the two modes have drifted apart and one of them is quietly wrong.
        var progressive = PricingEngine.Price(ProgressiveSlab(), 10m);
        var whole = PricingEngine.Price(ProgressiveSlab() with { SlabMode = SlabMode.WholeVolumeAtReachedBand }, 10m);

        progressive.UsageCharge.ShouldBe(whole.UsageCharge);
    }

    [Fact]
    public void Flat_rate_is_a_single_line()
    {
        var tariff = ProgressiveSlab() with
        {
            Mode = PricingMode.FlatRate,
            RatePerM3 = 32m,
            FixedCharge = 150m,
            Slabs = []
        };

        var result = PricingEngine.Price(tariff, 12.5m);

        result.Lines.Count.ShouldBe(1);
        result.UsageCharge.ShouldBe(400m);
        result.Total.ShouldBe(550m);
    }

    [Fact]
    public void Tax_applies_to_the_standing_charge_as_well_as_usage()
    {
        var result = PricingEngine.Price(ProgressiveSlab(taxPercent: 18m), 10m);

        result.Subtotal.ShouldBe(220m);
        result.TaxAmount.ShouldBe(39.60m);
        result.Total.ShouldBe(259.60m);
    }

    [Fact]
    public void Money_is_rounded_away_from_zero_to_two_places()
    {
        var tariff = ProgressiveSlab() with { Slabs = [new SlabSnapshot(1, 0m, null, 3.335m)] };

        // 1.5 x 3.335 = 5.0025 -> 5.00; the point is that the result is never a
        // fraction of a paisa that later fails to reconcile against a payment.
        PricingEngine.Price(tariff, 1.5m).UsageCharge.ShouldBe(5.00m);
    }

    [Fact]
    public void Negative_consumption_is_a_programming_error_not_a_credit_note()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PricingEngine.Price(ProgressiveSlab(), -1m));
    }

    [Fact]
    public void A_tariff_with_a_gap_between_bands_is_rejected_before_it_can_bill_anyone()
    {
        var tariff = ProgressiveSlab() with
        {
            Slabs = [new SlabSnapshot(1, 0m, 10m, 12m), new SlabSnapshot(2, 15m, null, 22m)]
        };

        var errors = TariffValidator.Validate(tariff);

        errors.ShouldNotBeEmpty();
        Should.Throw<InvalidTariffException>(() => PricingEngine.Price(tariff, 20m));
    }

    [Fact]
    public void A_tariff_whose_last_band_is_bounded_is_rejected()
    {
        var tariff = ProgressiveSlab() with { Slabs = [new SlabSnapshot(1, 0m, 10m, 12m)] };

        TariffValidator.Validate(tariff)
            .ShouldContain(e => e.Contains("open-ended", StringComparison.OrdinalIgnoreCase));
    }
}
