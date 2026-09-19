namespace WaterBilling.Infrastructure.Seeding;

/// <summary>
/// Fixed identifiers and credentials for the demo dataset.
/// <para>
/// Deterministic on purpose: the walkthrough in <c>docs/api-walkthrough.http</c>
/// can reference a meter key directly, so a reviewer gets from
/// <c>docker compose up</c> to a posted reading without first having to create
/// anything. These values only ever exist in a Development database.
/// </para>
/// </summary>
public static class DemoSeedData
{
    public static readonly Guid AdminUserId = new("00000000-0000-0000-0000-0000000000a1");

    public static readonly (Guid Id, string Email, string FullName, string Address)[] Customers =
    [
        (new Guid("00000000-0000-0000-0000-0000000000c1"), "asha@example.com", "Asha Menon", "14 Lake View, Block A"),
        (new Guid("00000000-0000-0000-0000-0000000000c2"), "ravi@example.com", "Ravi Kulkarni", "22 Hill Road, Block B"),
        (new Guid("00000000-0000-0000-0000-0000000000c3"), "leela@example.com", "Leela Fernandes", "7 Garden Lane, Block C")
    ];

    /// <summary>
    /// Demo meters. The last one is deliberately left unassigned so that a billing
    /// run has something to report as skipped rather than appearing suspiciously clean.
    /// </summary>
    public static readonly (Guid Id, string Serial, int CustomerIndex, string Location, string ApiKey)[] Meters =
    [
        (new Guid("00000000-0000-0000-0000-0000000000e1"), "WM-2024-0001", 0, "Flat A-101 kitchen riser", "wmk_demo0001_asha_flat_a101"),
        (new Guid("00000000-0000-0000-0000-0000000000e2"), "WM-2024-0002", 0, "Flat A-101 garden tap", "wmk_demo0002_asha_garden_tap"),
        (new Guid("00000000-0000-0000-0000-0000000000e3"), "WM-2024-0003", 1, "Flat B-204 main inlet", "wmk_demo0003_ravi_flat_b204"),
        (new Guid("00000000-0000-0000-0000-0000000000e4"), "WM-2024-0004", 2, "Villa C-7 main inlet", "wmk_demo0004_leela_villa_c7"),
        (new Guid("00000000-0000-0000-0000-0000000000e5"), "WM-2024-0005", 2, "Villa C-7 irrigation line", "wmk_demo0005_leela_irrigation"),
        (new Guid("00000000-0000-0000-0000-0000000000e6"), "WM-2024-0006", -1, "Block D commissioning spare (unassigned)", "wmk_demo0006_unassigned_spare")
    ];

    public static readonly Guid FlatRatePlanId = new("00000000-0000-0000-0000-0000000000f1");

    public static readonly Guid SlabPlanId = new("00000000-0000-0000-0000-0000000000f2");
}
