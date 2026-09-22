using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace WaterBilling.Api.Tests.Infrastructure;

/// <summary>A customer, a flat-rate tariff, and a meter with two readings on it — built through the same HTTP endpoints an admin uses, not by writing rows into the database directly.</summary>
public sealed record BillingScenario(
    Guid CustomerId,
    string CustomerEmail,
    string CustomerPassword,
    Guid MeterId,
    string MeterApiKey,
    Guid PricingPlanId);

/// <summary>
/// Builds the minimum estate needed to exercise a billing run: one customer, one
/// flat-rate plan, one meter with an opening and a closing reading. Every test that
/// needs a priceable meter starts here rather than repeating the same five HTTP
/// calls, and every value (serials, emails) is unique per call so tests sharing one
/// database never collide.
/// </summary>
public static class BillingScenarioBuilder
{
    public static async Task<BillingScenario> CreateAsync(
        WaterBillingApiFactory factory,
        HttpClient admin,
        string prefix,
        decimal ratePerM3,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc,
        decimal openingTotalM3,
        decimal closingTotalM3)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];

        var customerEmail = $"{prefix}-{unique}@test.local";
        const string customerPassword = "Integration#Test#Customer#12345";

        var customerResponse = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = customerEmail,
            password = customerPassword,
            fullName = $"{prefix} customer {unique}",
            role = "Customer",
        });
        customerResponse.EnsureSuccessStatusCode();
        var customerId = (await customerResponse.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();

        var planResponse = await admin.PostAsJsonAsync("/api/v1/pricing-plans", new
        {
            name = $"{prefix} flat plan {unique}",
            initialVersion = new
            {
                mode = "FlatRate",
                fixedCharge = 0m,
                ratePerM3,
                taxRatePercent = 0m,
                // Well before any period a test bills, so the version is always in
                // force by the time GenerateAsync resolves it.
                effectiveFromUtc = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
        });
        planResponse.EnsureSuccessStatusCode();
        var planId = (await planResponse.Content.ReadFromJsonAsync<JsonNode>())!["id"]!.GetValue<Guid>();

        var meterResponse = await admin.PostAsJsonAsync("/api/v1/meters", new
        {
            serialNumber = $"{prefix}-{unique}",
            customerId,
            pricingPlanId = planId,
            // Well before the billing period, or BillingService reports the meter
            // as installed after the period it is being billed for.
            installedAtUtc = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });
        meterResponse.EnsureSuccessStatusCode();
        var meterBody = await meterResponse.Content.ReadFromJsonAsync<JsonNode>();
        var meterId = meterBody!["meterId"]!.GetValue<Guid>();
        var meterApiKey = meterBody["apiKey"]!.GetValue<string>();

        using var meterClient = factory.CreateMeterClient(meterApiKey);

        // Opening reading: the latest reading at or before the period start.
        await PostReadingAsync(meterClient, periodStartUtc.AddDays(-1), openingTotalM3);

        // Closing reading: the latest reading at or before the period end. Placed at
        // the midpoint so it is unambiguously inside the period regardless of how
        // long a given test's period is.
        var closingReadingAtUtc = periodStartUtc + (periodEndUtc - periodStartUtc) / 2;
        await PostReadingAsync(meterClient, closingReadingAtUtc, closingTotalM3);

        return new BillingScenario(customerId, customerEmail, customerPassword, meterId, meterApiKey, planId);
    }

    private static async Task PostReadingAsync(HttpClient meterClient, DateTimeOffset readingAtUtc, decimal totalM3)
    {
        var response = await meterClient.PostAsJsonAsync("/api/v1/ingest/readings", new
        {
            readingAtUtc,
            totalM3,
        });

        response.EnsureSuccessStatusCode();
    }
}
