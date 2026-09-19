using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Readings;

namespace WaterBilling.Api.Features.Ingestion;

public static class IngestionEndpoints
{
    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ingest")
            .WithTags("Ingestion")
            .RequireAuthorization(AuthorizationPolicies.MeterDevice)
            .RequireRateLimiting(RateLimitingPolicies.Ingestion);

        group.MapPost("/readings", IngestSingleAsync)
            .WithName("IngestReading")
            .WithSummary("Submit one meter reading")
            .WithDescription(
                "Authenticated with the per-meter X-Meter-Key header. Idempotent: re-posting a reading " +
                "for a timestamp that already exists returns 200 with status 'duplicate_ignored' rather " +
                "than an error, so a device that retries after a timeout is always safe.")
            .WithValidation<ReadingRequest>()
            .Produces<ReadingIngestionResult>(StatusCodes.Status200OK)
            .Produces<ReadingIngestionResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/readings/batch", IngestBatchAsync)
            .WithName("IngestReadingBatch")
            .WithSummary("Submit buffered readings")
            .WithDescription(
                "For devices that store and forward after a link outage. Each reading is processed " +
                "independently, so one bad row does not discard the batch; the response reports the " +
                "outcome of every reading.")
            .WithValidation<ReadingBatchRequest>()
            .Produces<ReadingBatchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Created<ReadingIngestionResult>, Ok<ReadingIngestionResult>, ProblemHttpResult>> IngestSingleAsync(
        ReadingRequest request,
        ClaimsPrincipal principal,
        IngestionService ingestion,
        CancellationToken cancellationToken)
    {
        var meter = principal.GetMeterContext();

        var result = await ingestion.IngestAsync(meter.Id, request, ReadingSource.RestApi, cancellationToken);

        return result.Status switch
        {
            "accepted" => TypedResults.Created($"/api/v1/meters/{meter.Id}/readings/{result.ReadingId}", result),
            "duplicate_ignored" => TypedResults.Ok(result),
            _ => ApiProblems.UnprocessableEntity("Reading rejected", result.Message ?? "The reading could not be stored.", "reading-rejected")
        };
    }

    private static async Task<Ok<ReadingBatchResponse>> IngestBatchAsync(
        ReadingBatchRequest request,
        ClaimsPrincipal principal,
        IngestionService ingestion,
        CancellationToken cancellationToken)
    {
        var meter = principal.GetMeterContext();
        var results = new List<ReadingIngestionResult>(request.Readings.Count);

        // Oldest first, so that reset detection and the flow cross-check see the
        // series in the order the meter actually produced it.
        foreach (var reading in request.Readings.OrderBy(r => r.ReadingAtUtc))
        {
            results.Add(await ingestion.IngestAsync(meter.Id, reading, ReadingSource.RestApi, cancellationToken));
        }

        return TypedResults.Ok(new ReadingBatchResponse(
            meter.Id,
            meter.Serial,
            results.Count(r => r.Status == "accepted"),
            results.Count(r => r.Status == "duplicate_ignored"),
            results.Count(r => r.Status == "rejected"),
            results));
    }
}

public static class MeterPrincipalExtensions
{
    /// <summary>
    /// Reads the meter identity established by <see cref="MeterKeyAuthenticationHandler"/>.
    /// Throws rather than returning null: reaching a handler behind the meter-device
    /// policy without these claims would mean the pipeline is misconfigured.
    /// </summary>
    public static (Guid Id, string Serial) GetMeterContext(this ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(MeterClaimTypes.MeterId)
            ?? throw new InvalidOperationException("Meter identity claim is missing from an authenticated device request.");

        var serial = principal.FindFirstValue(MeterClaimTypes.MeterSerial) ?? "unknown";

        return (Guid.Parse(id), serial);
    }
}
