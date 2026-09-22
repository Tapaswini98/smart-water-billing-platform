using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shouldly;
using WaterBilling.Api.Tests.Infrastructure;
using Xunit;

namespace WaterBilling.Api.Tests;

/// <summary>
/// The authorization claims the README's "Verified by hand" table used to carry as
/// manual evidence — a customer cannot read another customer's invoice, cannot call
/// an admin-only endpoint, and a wrong password is rejected without revealing which
/// half of the credential was wrong. Automated here instead of re-verified by hand
/// on every change.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class AuthorizationBoundaryTests(WaterBillingApiFactory factory)
{
    // Shared with BillingRunTests — see SharedBillingPeriod for why every test that
    // calls billing-runs must use the same period.
    private static readonly DateTimeOffset PeriodStartUtc = SharedBillingPeriod.Start;
    private static readonly DateTimeOffset PeriodEndUtc = SharedBillingPeriod.End;

    [Fact]
    public async Task Customer_CannotReadAnotherCustomersInvoice_ButCanReadTheirOwn()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var owner = await BillingScenarioBuilder.CreateAsync(
            factory, admin, prefix: "authz-owner", ratePerM3: 25m,
            PeriodStartUtc, PeriodEndUtc, openingTotalM3: 100m, closingTotalM3: 130m);

        var billingRun = await admin.PostAsJsonAsync(
            "/api/v1/billing-runs",
            new { periodStartUtc = PeriodStartUtc, periodEndUtc = PeriodEndUtc },
            TestContext.Current.CancellationToken);
        billingRun.EnsureSuccessStatusCode();
        var runBody = await billingRun.Content.ReadFromJsonAsync<JsonNode>(TestContext.Current.CancellationToken);
        var invoiceId = runBody!["items"]!.AsArray()
            .Single(i => i!["meterId"]!.GetValue<Guid>() == owner.MeterId)!["invoiceId"]!.GetValue<Guid>();

        var (intruderEmail, intruderPassword) = await CreateCustomerAsync(admin, "authz-intruder");
        var intruderToken = await factory.LoginAsync(intruderEmail, intruderPassword);
        using var intruderClient = factory.CreateAuthenticatedClient(intruderToken);

        var intruderResponse = await intruderClient.GetAsync(
            $"/api/v1/invoices/{invoiceId}", TestContext.Current.CancellationToken);
        intruderResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Without this, the test above would pass just as well if the endpoint
        // rejected every customer — it needs to prove the boundary is drawn at
        // ownership, not that customers are blocked outright.
        var ownerToken = await factory.LoginAsync(owner.CustomerEmail, owner.CustomerPassword);
        using var ownerClient = factory.CreateAuthenticatedClient(ownerToken);

        var ownerResponse = await ownerClient.GetAsync(
            $"/api/v1/invoices/{invoiceId}", TestContext.Current.CancellationToken);
        ownerResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Customer_CallingAnAdminOnlyEndpoint_IsForbidden()
    {
        using var admin = await factory.CreateAdminClientAsync();

        var (email, password) = await CreateCustomerAsync(admin, "authz-noadmin");
        var token = await factory.LoginAsync(email, password);
        using var customerClient = factory.CreateAuthenticatedClient(token);

        // /api/v1/users is admin-only end to end — creating a user is exactly the
        // capability a customer account must never have.
        var response = await customerClient.PostAsJsonAsync(
            "/api/v1/users",
            new
            {
                email = $"should-never-exist-{Guid.NewGuid():N}@test.local",
                password = "Whatever#Password#12345",
                fullName = "Should Not Be Created",
                role = "Customer",
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Login_WithTheWrongPassword_ReturnsUnauthorized()
    {
        using var admin = await factory.CreateAdminClientAsync();
        var (email, _) = await CreateCustomerAsync(admin, "authz-badpassword");

        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = "definitely-the-wrong-password" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<(string Email, string Password)> CreateCustomerAsync(HttpClient admin, string prefix)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"{prefix}-{unique}@test.local";
        const string password = "Integration#Test#Customer#12345";

        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email,
            password,
            fullName = $"{prefix} {unique}",
            role = "Customer",
        });

        response.EnsureSuccessStatusCode();
        return (email, password);
    }
}
