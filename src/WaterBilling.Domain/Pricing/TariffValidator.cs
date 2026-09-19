namespace WaterBilling.Domain.Pricing;

/// <summary>
/// Structural rules a tariff must satisfy before it can be published. Enforced at
/// write time so the pricing engine never has to defend against a malformed band
/// set, and so an admin finds out at plan-creation rather than at billing-run.
/// </summary>
public static class TariffValidator
{
    public static IReadOnlyList<string> Validate(TariffSnapshot tariff)
    {
        ArgumentNullException.ThrowIfNull(tariff);

        var errors = new List<string>();

        if (tariff.FixedCharge < 0m)
        {
            errors.Add("Fixed charge cannot be negative.");
        }

        if (tariff.TaxRatePercent is < 0m or > 100m)
        {
            errors.Add("Tax rate must be between 0 and 100 percent.");
        }

        if (tariff.Mode is PricingMode.FlatRate)
        {
            if (tariff.RatePerM3 < 0m)
            {
                errors.Add("Rate per m3 cannot be negative.");
            }

            return errors;
        }

        var slabs = tariff.Slabs.OrderBy(s => s.SortOrder).ToArray();

        if (slabs.Length == 0)
        {
            errors.Add("A slab plan must define at least one band.");
            return errors;
        }

        if (slabs[0].FromM3 != 0m)
        {
            errors.Add($"The first band must start at 0 m3, but starts at {slabs[0].FromM3}.");
        }

        for (var i = 0; i < slabs.Length; i++)
        {
            var slab = slabs[i];

            if (slab.RatePerM3 < 0m)
            {
                errors.Add($"Band {i + 1} has a negative rate.");
            }

            if (slab.ToM3 is { } to && to <= slab.FromM3)
            {
                errors.Add($"Band {i + 1} ends at {to} m3, which is not above its start of {slab.FromM3} m3.");
            }

            var isLast = i == slabs.Length - 1;

            if (!isLast)
            {
                if (slab.ToM3 is null)
                {
                    errors.Add($"Band {i + 1} is open-ended but is not the last band.");
                    continue;
                }

                if (slabs[i + 1].FromM3 != slab.ToM3)
                {
                    errors.Add(
                        $"Bands {i + 1} and {i + 2} are not contiguous: band {i + 1} ends at " +
                        $"{slab.ToM3} m3 but band {i + 2} starts at {slabs[i + 1].FromM3} m3.");
                }
            }
            else if (slab.ToM3 is not null)
            {
                errors.Add("The last band must be open-ended (no upper bound) so that any consumption is priceable.");
            }
        }

        return errors;
    }
}
