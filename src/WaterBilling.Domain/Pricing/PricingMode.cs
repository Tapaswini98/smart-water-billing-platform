namespace WaterBilling.Domain.Pricing;

public enum PricingMode
{
    /// <summary>One rate per m3 regardless of volume, plus the fixed standing charge.</summary>
    FlatRate = 1,

    /// <summary>Banded rates, like an electricity tariff. See <see cref="SlabMode"/>.</summary>
    Slab = 2
}
