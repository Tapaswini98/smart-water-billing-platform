namespace WaterBilling.Infrastructure.Seeding;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>
    /// Off unless explicitly enabled, and refused outright outside Development.
    /// A seeder that can run in production is a data-loss incident waiting for a
    /// misconfigured environment variable.
    /// </summary>
    public bool Enabled { get; set; }

    public string AdminEmail { get; set; } = "admin@waterworks.local";

    public string AdminPassword { get; set; } = "Admin#12345";

    public string CustomerPassword { get; set; } = "Customer#12345";
}
