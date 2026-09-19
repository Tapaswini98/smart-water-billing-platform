using System.Security.Claims;
using WaterBilling.Domain.Users;

namespace WaterBilling.Api.Infrastructure;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated user's id. Throws when absent: every call site sits behind an
    /// authorization policy, so a missing subject claim is a pipeline bug, not a
    /// condition a handler should try to recover from.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated principal has no subject claim.");

        return Guid.Parse(value);
    }

    public static bool IsAdmin(this ClaimsPrincipal principal) =>
        principal.IsInRole(nameof(UserRole.Admin));
}
