using System.Globalization;

namespace WaterBilling.Domain.Pricing;

/// <summary>
/// Prices a volume against a tariff. Pure, allocation-light, and deliberately free
/// of any notion of an invoice, a meter or a database — which is why its boundary
/// behaviour is covered by table-driven tests rather than by integration runs.
/// </summary>
public static class PricingEngine
{
    /// <summary>Currency amounts are held to 2 decimal places.</summary>
    public const int MoneyDecimals = 2;

    /// <summary>Volumes are held to 3 decimal places (1 litre resolution on m3).</summary>
    public const int VolumeDecimals = 3;

    /// <summary>
    /// Away-from-zero, not banker's rounding. Utility billing convention, and it
    /// avoids the "why is 0.125 sometimes 0.12" support ticket.
    /// </summary>
    private const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public static PriceBreakdown Price(TariffSnapshot tariff, decimal consumptionM3)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentOutOfRangeException.ThrowIfNegative(consumptionM3);

        var errors = TariffValidator.Validate(tariff);
        if (errors.Count > 0)
        {
            throw new InvalidTariffException(tariff.PlanVersionId, errors);
        }

        var consumption = Math.Round(consumptionM3, VolumeDecimals, Rounding);

        var lines = tariff.Mode switch
        {
            PricingMode.FlatRate => PriceFlat(tariff, consumption),
            PricingMode.Slab when tariff.SlabMode is SlabMode.Progressive => PriceProgressive(tariff, consumption),
            PricingMode.Slab => PriceWholeVolumeAtReachedBand(tariff, consumption),
            _ => throw new ArgumentOutOfRangeException(nameof(tariff), tariff.Mode, "Unsupported pricing mode.")
        };

        var fixedCharge = Money(tariff.FixedCharge);
        var usageCharge = Money(lines.Sum(l => l.Amount));
        var subtotal = fixedCharge + usageCharge;
        var tax = Money(subtotal * tariff.TaxRatePercent / 100m);

        return new PriceBreakdown
        {
            PlanId = tariff.PlanId,
            PlanVersionId = tariff.PlanVersionId,
            PlanVersionNumber = tariff.VersionNumber,
            PlanName = tariff.PlanName,
            Currency = tariff.Currency,
            ConsumptionM3 = consumption,
            Lines = lines,
            FixedCharge = fixedCharge,
            UsageCharge = usageCharge,
            TaxRatePercent = tariff.TaxRatePercent,
            TaxAmount = tax
        };
    }

    private static List<PriceLine> PriceFlat(TariffSnapshot tariff, decimal consumption) =>
    [
        new PriceLine(
            SortOrder: 1,
            Description: $"Water consumption @ {Format(tariff.RatePerM3)}/m3",
            FromM3: null,
            ToM3: null,
            UnitsM3: consumption,
            RatePerM3: tariff.RatePerM3,
            Amount: Money(consumption * tariff.RatePerM3))
    ];

    private static List<PriceLine> PriceProgressive(TariffSnapshot tariff, decimal consumption)
    {
        var lines = new List<PriceLine>(tariff.Slabs.Count);

        foreach (var slab in tariff.Slabs.OrderBy(s => s.SortOrder))
        {
            // Units of this band that were actually used, given the band covers (From, To].
            var upper = slab.ToM3 is { } to ? Math.Min(consumption, to) : consumption;
            var units = Math.Round(Math.Max(0m, upper - slab.FromM3), VolumeDecimals, Rounding);

            // Emit zero-unit bands only when nothing has been consumed at all, so a
            // zero-consumption invoice still shows the tariff structure it was billed under.
            if (units <= 0m && consumption > 0m)
            {
                continue;
            }

            lines.Add(new PriceLine(
                SortOrder: slab.SortOrder,
                Description: DescribeBand(slab),
                FromM3: slab.FromM3,
                ToM3: slab.ToM3,
                UnitsM3: units,
                RatePerM3: slab.RatePerM3,
                Amount: Money(units * slab.RatePerM3)));

            if (slab.ToM3 is { } bandEnd && consumption <= bandEnd)
            {
                break;
            }
        }

        return lines;
    }

    private static List<PriceLine> PriceWholeVolumeAtReachedBand(TariffSnapshot tariff, decimal consumption)
    {
        var ordered = tariff.Slabs.OrderBy(s => s.SortOrder).ToArray();

        // The band the total volume lands in, using the same (From, To] convention
        // as progressive pricing so the two modes agree on where a boundary sits.
        var reached = ordered.FirstOrDefault(
            s => consumption > s.FromM3 && (s.ToM3 is null || consumption <= s.ToM3),
            ordered[0]);

        return
        [
            new PriceLine(
                SortOrder: reached.SortOrder,
                Description: $"Water consumption, whole volume @ {DescribeBand(reached)}",
                FromM3: reached.FromM3,
                ToM3: reached.ToM3,
                UnitsM3: consumption,
                RatePerM3: reached.RatePerM3,
                Amount: Money(consumption * reached.RatePerM3))
        ];
    }

    private static string DescribeBand(SlabSnapshot slab) => slab.ToM3 is { } to
        ? $"{Format(slab.FromM3)}-{Format(to)} m3 @ {Format(slab.RatePerM3)}/m3"
        : $"Above {Format(slab.FromM3)} m3 @ {Format(slab.RatePerM3)}/m3";

    private static decimal Money(decimal value) => Math.Round(value, MoneyDecimals, Rounding);

    private static string Format(decimal value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
