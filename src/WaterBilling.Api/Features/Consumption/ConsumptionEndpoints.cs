using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Billing;
using WaterBilling.Domain.Common;
using WaterBilling.Domain.Readings;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Consumption;

public static class ConsumptionEndpoints
{
    public static IEndpointRouteBuilder MapConsumptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/meters")
            .WithTags("Consumption")
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedUser);

        group.MapGet("/{meterId:guid}/consumption", GetConsumptionAsync)
            .WithName("GetMeterConsumption")
            .WithSummary("View consumption for a period")
            .WithDescription(
                "Derives consumption from the stored reading series rather than a running counter: " +
                "TOTAL at the last reading on or before the period end, minus TOTAL at the last reading " +
                "on or before the period start, walked pairwise so a meter reset is summed across. " +
                "Defaults to the current calendar month. Flags explain any caveat on the figure.")
            .Produces<ConsumptionResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{meterId:guid}/consumption/monthly", GetMonthlyAsync)
            .WithName("GetMeterMonthlyConsumption")
            .WithSummary("Consumption month by month")
            .WithDescription("A rolling series for charting. Each month is computed independently with the same formula, so the months sum exactly to the total across the span.")
            .Produces<MonthlyConsumptionResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<Results<Ok<ConsumptionResponse>, ProblemHttpResult>> GetConsumptionAsync(
        Guid meterId,
        ClaimsPrincipal principal,
        WaterBillingDbContext db,
        BillingCalendar calendar,
        CancellationToken cancellationToken,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var access = await MeterAccess.CheckAsync(db, meterId, principal, cancellationToken);
        if (access.Problem is { } problem)
        {
            return problem;
        }

        var period = from is { } start && to is { } end
            ? new DateRange(start, end)
            : calendar.CurrentMonth();

        if (period.End <= period.Start)
        {
            return ApiProblems.UnprocessableEntity(
                "Invalid period",
                $"The period end ({period.End:O}) must be after its start ({period.Start:O}).",
                "invalid-period");
        }

        var result = await ComputeAsync(db, meterId, period, cancellationToken);

        return TypedResults.Ok(ToResponse(meterId, access.SerialNumber, result));
    }

    private static async Task<Results<Ok<MonthlyConsumptionResponse>, ProblemHttpResult>> GetMonthlyAsync(
        Guid meterId,
        ClaimsPrincipal principal,
        WaterBillingDbContext db,
        BillingCalendar calendar,
        CancellationToken cancellationToken,
        int months = 6)
    {
        var access = await MeterAccess.CheckAsync(db, meterId, principal, cancellationToken);
        if (access.Problem is { } problem)
        {
            return problem;
        }

        months = Math.Clamp(months, 1, 36);

        var periods = calendar.LastMonths(months);
        var earliest = periods[0].Start;
        // Hoisted out of the query below: EF cannot translate a from-end index
        // (`periods[^1]`) inside an expression tree.
        var latest = periods[periods.Count - 1].End;

        // One query for the whole span, then each month is computed in memory. At the
        // design cadence this is a few thousand rows; issuing one query per month
        // would multiply round trips for no benefit.
        var readings = await db.MeterReadings
            .AsNoTracking()
            .Where(r => r.MeterId == meterId && r.ReadingAtUtc <= latest)
            .OrderBy(r => r.ReadingAtUtc)
            .Select(r => new ReadingPoint(r.ReadingAtUtc, r.TotalM3, r.FlowM3PerHour))
            .ToListAsync(cancellationToken);

        var series = periods
            .Select(period => ToResponse(meterId, access.SerialNumber,
                ConsumptionCalculator.Calculate(readings, period)))
            .ToList();

        return TypedResults.Ok(new MonthlyConsumptionResponse(
            meterId,
            access.SerialNumber,
            earliest,
            latest,
            series.Sum(s => s.ConsumptionM3),
            series));
    }

    internal static async Task<ConsumptionResult> ComputeAsync(
        WaterBillingDbContext db,
        Guid meterId,
        DateRange period,
        CancellationToken cancellationToken)
    {
        // Readings from the period, plus the last one before it. That trailing reading
        // is the opening anchor — without it a meter that reported nothing early in the
        // month would appear to have consumed from zero.
        var readings = await db.MeterReadings
            .AsNoTracking()
            .Where(r => r.MeterId == meterId && r.ReadingAtUtc <= period.End)
            .OrderByDescending(r => r.ReadingAtUtc)
            .Select(r => new ReadingPoint(r.ReadingAtUtc, r.TotalM3, r.FlowM3PerHour))
            .Take(MaxReadingsPerPeriod)
            .ToListAsync(cancellationToken);

        return ConsumptionCalculator.Calculate(readings, period);
    }

    /// <summary>
    /// A month at the design cadence is ~2,900 readings. This cap is generous enough
    /// for a badly configured meter reporting every few seconds, while still bounding
    /// the memory a single billing run can consume.
    /// </summary>
    private const int MaxReadingsPerPeriod = 20_000;

    private static ConsumptionResponse ToResponse(Guid meterId, string serial, ConsumptionResult result) => new(
        meterId,
        serial,
        result.Period.Start,
        result.Period.End,
        result.ConsumptionM3,
        result.OpeningTotalM3,
        result.ClosingTotalM3,
        result.OpeningReadingAtUtc,
        result.ClosingReadingAtUtc,
        result.ReadingCount,
        result.ResetCount,
        result.Flags is ConsumptionFlags.None ? [] : [.. Enum.GetValues<ConsumptionFlags>()
            .Where(f => f != ConsumptionFlags.None && result.Flags.HasFlag(f))
            .Select(f => f.ToString())],
        result.IsBillableWithConfidence);
}

public sealed record ConsumptionResponse(
    Guid MeterId,
    string SerialNumber,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    decimal ConsumptionM3,
    decimal? OpeningTotalM3,
    decimal? ClosingTotalM3,
    DateTimeOffset? OpeningReadingAtUtc,
    DateTimeOffset? ClosingReadingAtUtc,
    int ReadingCount,
    int ResetCount,
    IReadOnlyList<string> Flags,
    bool IsBillableWithConfidence);

public sealed record MonthlyConsumptionResponse(
    Guid MeterId,
    string SerialNumber,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    decimal TotalConsumptionM3,
    IReadOnlyList<ConsumptionResponse> Months);
