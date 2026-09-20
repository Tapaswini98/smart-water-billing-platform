using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Infrastructure;

/// <summary>
/// "Does this caller own this meter?" — asked by every customer-facing meter route.
/// Written once so the rule cannot be applied inconsistently across features, which
/// is the usual way a data-leak bug gets in.
/// </summary>
public static class MeterAccess
{
    public readonly record struct Result(string SerialNumber, Guid? CustomerId, ProblemHttpResult? Problem);

    public static async Task<Result> CheckAsync(
        WaterBillingDbContext db,
        Guid meterId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var meter = await db.Meters
            .AsNoTracking()
            .Where(m => m.Id == meterId)
            .Select(m => new { m.SerialNumber, m.CustomerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (meter is null)
        {
            return new Result(string.Empty, null, ApiProblems.NotFound($"No meter exists with id {meterId}.", "Meter"));
        }

        if (!principal.IsAdmin() && meter.CustomerId != principal.GetUserId())
        {
            // 403 rather than 404: the meter's existence is not itself a secret, and
            // pretending otherwise makes legitimate support calls harder.
            return new Result(meter.SerialNumber, meter.CustomerId,
                ApiProblems.Forbidden($"Meter {meter.SerialNumber} is not assigned to your account."));
        }

        return new Result(meter.SerialNumber, meter.CustomerId, null);
    }
}
