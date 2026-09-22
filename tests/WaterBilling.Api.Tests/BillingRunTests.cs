using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using WaterBilling.Api.Tests.Infrastructure;
using Xunit;

namespace WaterBilling.Api.Tests;

/// <summary>
/// ADR-0007's contract end to end: a billing run prices consumption correctly from
/// real ingested readings, and re-running the same period is a no-op rather than a
/// second invoice. These are the two rows the README's "Verified by hand" table
/// used to carry as manual evidence; this is what replaces that evidence with a
/// repeatable check.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class BillingRunTests(WaterBillingApiFactory factory)
{
    private static readonly DateTimeOffset PeriodStartUtc = SharedBillingPeriod.Start;
    private static readonly DateTimeOffset PeriodEndUtc = SharedBillingPeriod.End;

    [Fact]
    public async Task GenerateInvoices_PricesRealConsumptionCorrectly_AndReRunningTheSamePeriodChangesNothing()
    {
        using var admin = await factory.CreateAdminClientAsync();

        // 40 m3 of consumption (140 - 100) at a flat 25/m3 should bill exactly 1000,
        // with no fixed charge and no tax — a total simple enough to assert on
        // exactly, so a pricing regression shows up as a wrong number, not just a
        // wrong status code.
        var scenario = await BillingScenarioBuilder.CreateAsync(
            factory,
            admin,
            prefix: "billing-run",
            ratePerM3: 25m,
            PeriodStartUtc,
            PeriodEndUtc,
            openingTotalM3: 100m,
            closingTotalM3: 140m);

        // --- Phase 1: the first run generates one correctly priced invoice. ---

        var firstRun = await admin.PostAsJsonAsync(
            "/api/v1/billing-runs",
            new { periodStartUtc = PeriodStartUtc, periodEndUtc = PeriodEndUtc },
            TestContext.Current.CancellationToken);

        firstRun.StatusCode.ShouldBe(HttpStatusCode.Created);
        // Not asserting invoicesGenerated/skipped/failed here: this period is shared
        // across every test that calls billing-runs (see SharedBillingPeriod), so
        // other tests' meters are legitimately present in the same run. Only this
        // meter's own item is this test's business.
        var firstRunBody = await firstRun.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken);

        var items = firstRunBody!["items"]!.AsArray();
        var item = items.Single(i => i!["meterId"]!.GetValue<Guid>() == scenario.MeterId)!;

        item["outcome"]!.GetValue<string>().ShouldBe("Generated");
        item["consumptionM3"]!.GetValue<decimal>().ShouldBe(40m);
        item["totalAmount"]!.GetValue<decimal>().ShouldBe(1000m);

        var invoiceId = item["invoiceId"]!.GetValue<Guid>();

        var invoiceResponse = await admin.GetAsync(
            $"/api/v1/invoices/{invoiceId}", TestContext.Current.CancellationToken);
        invoiceResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var invoice = await invoiceResponse.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken);

        invoice!["consumptionM3"]!.GetValue<decimal>().ShouldBe(40m);
        invoice["usageCharge"]!.GetValue<decimal>().ShouldBe(1000m);
        invoice["totalAmount"]!.GetValue<decimal>().ShouldBe(1000m);
        invoice["status"]!.GetValue<string>().ShouldBe("Issued");

        // --- Phase 2: running the exact same period again must not double-bill. ---

        var secondRun = await admin.PostAsJsonAsync(
            "/api/v1/billing-runs",
            new { periodStartUtc = PeriodStartUtc, periodEndUtc = PeriodEndUtc },
            TestContext.Current.CancellationToken);

        secondRun.StatusCode.ShouldBe(HttpStatusCode.Created);
        var secondRunBody = await secondRun.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken);

        var secondItems = secondRunBody!["items"]!.AsArray();
        var secondItem = secondItems.Single(i => i!["meterId"]!.GetValue<Guid>() == scenario.MeterId)!;

        secondItem["outcome"]!.GetValue<string>().ShouldBe("AlreadyBilled");
        // Same invoice, not a new one: the unique index made a second one
        // impossible, and this confirms the run reported that rather than erroring.
        secondItem["invoiceId"]!.GetValue<Guid>().ShouldBe(invoiceId);

        var invoicesForMeter = await admin.GetAsync(
            $"/api/v1/invoices?meterId={scenario.MeterId}", TestContext.Current.CancellationToken);
        var invoiceList = await invoicesForMeter.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken);
        invoiceList!["totalCount"]!.GetValue<int>().ShouldBe(1);
    }

    [Fact]
    public async Task GenerateInvoices_IdenticalOpeningAndClosingReadings_StillGeneratesAZeroUsageInvoice()
    {
        using var admin = await factory.CreateAdminClientAsync();

        // Opening and closing totals are equal — a meter that genuinely used
        // nothing, not a meter with missing data (that is a different code path,
        // ConsumptionFlags.NoData, which needs no reading at or before the period
        // at all; BillingScenarioBuilder always posts one). Worth a dedicated case
        // because zero is the value most likely to be mistaken for "no data" or
        // "skip this meter" by an off-by-one in the flag logic.
        var scenario = await BillingScenarioBuilder.CreateAsync(
            factory,
            admin,
            prefix: "billing-zero-usage",
            ratePerM3: 25m,
            SharedBillingPeriod.Start,
            SharedBillingPeriod.End,
            openingTotalM3: 100m,
            closingTotalM3: 100m);

        var run = await admin.PostAsJsonAsync(
            "/api/v1/billing-runs",
            new { periodStartUtc = SharedBillingPeriod.Start, periodEndUtc = SharedBillingPeriod.End },
            TestContext.Current.CancellationToken);

        run.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await run.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken);
        var item = body!["items"]!.AsArray().Single(i => i!["meterId"]!.GetValue<Guid>() == scenario.MeterId)!;

        // Generated, not Skipped or Failed: a customer who used nothing still gets
        // an invoice for the standing charge, and the run must not mistake "zero"
        // for "missing".
        item["outcome"]!.GetValue<string>().ShouldBe("Generated");
        item["consumptionM3"]!.GetValue<decimal>().ShouldBe(0m);
        item["totalAmount"]!.GetValue<decimal>().ShouldBe(0m);
    }
}
