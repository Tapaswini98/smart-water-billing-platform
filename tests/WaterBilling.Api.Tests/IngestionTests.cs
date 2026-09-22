using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WaterBilling.Api.Tests.Infrastructure;
using WaterBilling.Infrastructure.Persistence;
using Xunit;

namespace WaterBilling.Api.Tests;

/// <summary>
/// The guarantee ADR-0007 and the ingestion endpoint's own description promise: a
/// meter that retries a reading after a timeout is always safe, because re-posting
/// the same (meter, timestamp) pair is a no-op rather than a duplicate row or an
/// error. Verified by hand against a live database before (see the README's
/// "Verified by hand" table); this automates exactly that check.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class IngestionTests(WaterBillingApiFactory factory)
{
    [Fact]
    public async Task IngestReading_PostedTwice_SecondPostIsIgnoredAndNoDuplicateRowIsStored()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var scenario = await BillingScenarioBuilder.CreateAsync(
            factory,
            admin,
            prefix: "ingest-idempotency",
            ratePerM3: 10m,
            // Relative to "now": the ingestion endpoint rejects anything older than
            // its 35-day backdating limit, so a fixed calendar date would eventually
            // start failing on its own regardless of what year was picked.
            periodStartUtc: DateTimeOffset.UtcNow.Date.AddDays(-12),
            periodEndUtc: DateTimeOffset.UtcNow.Date.AddDays(-5),
            openingTotalM3: 500m,
            closingTotalM3: 520m);

        using var meterClient = factory.CreateMeterClient(scenario.MeterApiKey);
        var readingAtUtc = DateTimeOffset.UtcNow.Date.AddDays(-9).AddHours(9);

        var first = await meterClient.PostAsJsonAsync(
            "/api/v1/ingest/readings",
            new { readingAtUtc, totalM3 = 510m },
            TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken);
        firstBody!["status"]!.GetValue<string>().ShouldBe("accepted");
        var readingId = firstBody["readingId"]!.GetValue<Guid>();

        var second = await meterClient.PostAsJsonAsync(
            "/api/v1/ingest/readings",
            new { readingAtUtc, totalM3 = 510m },
            TestContext.Current.CancellationToken);

        // 200, not 201: nothing new was created, so this is not the same response
        // as the first post.
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken);
        secondBody!["status"]!.GetValue<string>().ShouldBe("duplicate_ignored");

        // The unique index is the actual guarantee; the status code is only a
        // report of what it did. Checking the row count directly is what would
        // catch a regression where the endpoint returns the right status but a
        // second row slipped in anyway.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WaterBillingDbContext>();
        var matchingReadings = await db.MeterReadings
            .Where(r => r.MeterId == scenario.MeterId && r.ReadingAtUtc == readingAtUtc)
            .ToListAsync(TestContext.Current.CancellationToken);

        matchingReadings.Count.ShouldBe(1);
        matchingReadings.Single().Id.ShouldBe(readingId);
    }

    [Fact]
    public async Task IngestReading_WithoutAMeterKey_IsRejectedBeforeItReachesTheDatabase()
    {
        using var anonymousClient = factory.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync(
            "/api/v1/ingest/readings",
            new { readingAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), totalM3 = 100m },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IngestReading_WithAUserJwtInsteadOfAMeterKey_IsRejected()
    {
        // A human's bearer token authenticates the JwtBearer scheme; the ingestion
        // endpoint only accepts the MeterDevice scheme. This is the authorization
        // boundary ADR-0005 exists for: a device credential and a human credential
        // must never be interchangeable.
        using var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/v1/ingest/readings",
            new { readingAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), totalM3 = 100m },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
