namespace WaterBilling.Api.Infrastructure;

public static class AuthorizationPolicies
{
    /// <summary>Human administrators, authenticated with a JWT.</summary>
    public const string AdminOnly = "admin-only";

    /// <summary>Any authenticated human (admin or customer).</summary>
    public const string AuthenticatedUser = "authenticated-user";

    /// <summary>Devices authenticated with a per-meter API key. Never satisfied by a user JWT.</summary>
    public const string MeterDevice = "meter-device";
}

public static class MeterClaimTypes
{
    public const string MeterId = "meter_id";

    public const string MeterSerial = "meter_serial";
}
