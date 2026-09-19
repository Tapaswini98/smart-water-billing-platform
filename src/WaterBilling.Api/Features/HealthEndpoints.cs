using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features;

/// <summary>
/// Two probes with different jobs. <c>/health/live</c> answers "is the process up?"
/// and must never touch a dependency — a liveness probe that fails because the
/// database is down gets the application restarted for no reason. <c>/health/ready</c>
/// answers "can it serve traffic?" and therefore does check the database.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/health").WithTags("Health").AllowAnonymous().ExcludeFromDescription();

        group.MapGet("/live", () => TypedResults.Ok(new { status = "healthy" }));

        group.MapGet("/ready", async Task<Results<Ok<HealthResponse>, ProblemHttpResult>> (
            WaterBillingDbContext db,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var canConnect = await db.Database.CanConnectAsync(cancellationToken);
                var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);

                if (!canConnect)
                {
                    return TypedResults.Problem(
                        title: "Database unreachable",
                        detail: "The API is running but cannot reach PostgreSQL.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                return TypedResults.Ok(new HealthResponse("healthy", true, pending.Count()));
            }
            catch (Exception ex)
            {
                return TypedResults.Problem(
                    title: "Readiness check failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return app;
    }
}

public sealed record HealthResponse(string Status, bool DatabaseReachable, int PendingMigrations);
