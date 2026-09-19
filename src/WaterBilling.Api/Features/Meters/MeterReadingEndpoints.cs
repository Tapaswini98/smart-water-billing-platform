using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Users;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Meters;

/// <summary>
/// The read side of the ingestion slice. Kept here rather than under Ingestion
/// because it is a user-facing query with user-facing authorization, not a device path.
/// </summary>
public static class MeterReadingEndpoints
{
    /// <summary>Caps an unbounded scan; a year of 15-minute readings is ~35,000 rows.</summary>
    private const int MaxPageSize = 500;

    public static IEndpointRouteBuilder MapMeterReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/meters")
            .WithTags("Meters")
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedUser);

        group.MapGet("/{meterId:guid}/readings", GetReadingsAsync)
            .WithName("GetMeterReadings")
            .WithSummary("List readings for a meter")
            .WithDescription(
                "Admins may read any meter. A customer may read only meters assigned to their own " +
                "account; requesting another customer's meter returns 403, not 404, because the " +
                "meter's existence is not itself a secret.")
            .Produces<ReadingPageResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<ReadingPageResponse>, ProblemHttpResult>> GetReadingsAsync(
        Guid meterId,
        ClaimsPrincipal principal,
        WaterBillingDbContext db,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int pageSize = 100,
        int page = 1)
    {
        var meter = await db.Meters
            .AsNoTracking()
            .Where(m => m.Id == meterId)
            .Select(m => new { m.Id, m.SerialNumber, m.CustomerId })
            .FirstOrDefaultAsync(cancellationToken);

        if (meter is null)
        {
            return ApiProblems.NotFound($"No meter exists with id {meterId}.", "Meter");
        }

        if (!principal.IsInRole(nameof(UserRole.Admin)))
        {
            var callerId = principal.GetUserId();

            if (meter.CustomerId != callerId)
            {
                return ApiProblems.Forbidden($"Meter {meter.SerialNumber} is not assigned to your account.");
            }
        }

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.MeterReadings.AsNoTracking().Where(r => r.MeterId == meterId);

        if (from is { } fromUtc)
        {
            query = query.Where(r => r.ReadingAtUtc >= fromUtc);
        }

        if (to is { } toUtc)
        {
            query = query.Where(r => r.ReadingAtUtc < toUtc);
        }

        var total = await query.CountAsync(cancellationToken);

        var readings = await query
            .OrderByDescending(r => r.ReadingAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ReadingResponse(
                r.Id,
                r.ReadingAtUtc,
                r.ReceivedAtUtc,
                r.TotalM3,
                r.FlowM3PerHour,
                r.Source.ToString(),
                r.Anomalies.ToString(),
                r.AnomalyNotes))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ReadingPageResponse(
            meter.Id, meter.SerialNumber, page, pageSize, total, readings));
    }
}

public sealed record ReadingResponse(
    Guid Id,
    DateTimeOffset ReadingAtUtc,
    DateTimeOffset ReceivedAtUtc,
    decimal TotalM3,
    decimal? FlowM3PerHour,
    string Source,
    string Anomalies,
    string? AnomalyNotes);

public sealed record ReadingPageResponse(
    Guid MeterId,
    string SerialNumber,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<ReadingResponse> Readings);
