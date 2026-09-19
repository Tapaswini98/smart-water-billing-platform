namespace WaterBilling.Domain.Pricing;

/// <summary>
/// "Slab based (like electricity meters)" is ambiguous between two real-world
/// tariffs, so the interpretation is configuration, not a hard-coded assumption
/// (ADR-0004). <see cref="Progressive"/> is the default.
/// </summary>
public enum SlabMode
{
    /// <summary>
    /// Telescopic: each band charges only the units that fall inside it.
    /// 25 m3 over bands 0-10 @ 5, 10-30 @ 8 costs (10 x 5) + (15 x 8).
    /// </summary>
    Progressive = 1,

    /// <summary>
    /// Non-telescopic: the whole volume is charged at the rate of the band it lands in.
    /// The same 25 m3 costs 25 x 8. Used by some municipal water tariffs.
    /// </summary>
    WholeVolumeAtReachedBand = 2
}
