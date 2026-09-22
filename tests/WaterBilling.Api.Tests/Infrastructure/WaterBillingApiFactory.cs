using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using WaterBilling.Domain.Users;
using WaterBilling.Infrastructure.Auth;
using WaterBilling.Infrastructure.Persistence;
using Xunit;

namespace WaterBilling.Api.Tests.Infrastructure;

/// <summary>
/// One real PostgreSQL instance (via Testcontainers) and one real host, shared
/// across the whole suite through <see cref="ApiTestCollection"/>.
/// <para>
/// Migrations run exactly as they do in production — through
/// <c>DatabaseInitializer</c>, the same hosted service, not a hand-rolled test-only
/// path — so a migration that would break a real deployment breaks this fixture
/// too, at fixture start rather than after everything looked fine.
/// </para>
/// <para>
/// The environment is set to "Testing", not "Development", so the 44,000-reading
/// demo seed never runs here. Every fixture this suite needs is created through the
/// same HTTP endpoints a real admin uses, except the one admin account below that
/// bootstraps the rest — creating a user requires an admin token already, so
/// something has to break that circularity.
/// </para>
/// </summary>
public sealed class WaterBillingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TestSigningKey =
        "integration-test-signing-key-not-used-anywhere-outside-this-test-run";

    public const string SeedAdminEmail = "integration-admin@test.local";
    public const string SeedAdminPassword = "Integration#Test#Admin#12345";

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("waterbilling_tests")
        .WithUsername("waterbilling")
        .WithPassword("waterbilling")
        .Build();

    private string? cachedAdminToken;

    public async ValueTask InitializeAsync()
    {
        await postgres.StartAsync();

        // Environment variables, not ConfigureAppConfiguration: Program.cs reads
        // ConnectionStrings:WaterBilling synchronously, inside AddInfrastructure's
        // eager validation, while the top-level statements run — before
        // WebApplicationFactory's ConfigureAppConfiguration hook gets a chance to
        // contribute a source for a minimal-hosting Program.cs. Environment
        // variables are read by WebApplication.CreateBuilder itself, at the same
        // point docker-compose.yml's ConnectionStrings__WaterBilling already relies
        // on in production, so this is the one override channel guaranteed to land
        // before that read happens.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
        Environment.SetEnvironmentVariable("ConnectionStrings__WaterBilling", postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("Jwt__Issuer", "waterbilling-tests");
        Environment.SetEnvironmentVariable("Jwt__Audience", "waterbilling-tests-clients");
        Environment.SetEnvironmentVariable("Seed__Enabled", "false");

        // Building the host now, rather than lazily on the first test's first
        // request, is what makes DatabaseInitializer apply migrations here instead
        // of mid-test.
        using var warmup = CreateClient();

        await SeedAdminUserAsync();
    }

    private async Task SeedAdminUserAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WaterBillingDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        db.Users.Add(new User
        {
            Email = SeedAdminEmail,
            FullName = "Integration Test Admin",
            PasswordHash = hasher.Hash(SeedAdminPassword),
            Role = UserRole.Admin,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Cached so every test that needs an admin token reuses the same one instead
    /// of logging in again — which is also what keeps the whole suite comfortably
    /// under the authentication endpoint's rate limit.
    /// </summary>
    public async Task<string> GetAdminTokenAsync() =>
        cachedAdminToken ??= await LoginAsync(SeedAdminEmail, SeedAdminPassword);

    public async Task<string> LoginAsync(string email, string password)
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password,
        });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        return body!["accessToken"]!.GetValue<string>();
    }

    public HttpClient CreateAuthenticatedClient(string bearerToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    public async Task<HttpClient> CreateAdminClientAsync() =>
        CreateAuthenticatedClient(await GetAdminTokenAsync());

    public HttpClient CreateMeterClient(string meterApiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(MeterApiKeyGenerator.HeaderName, meterApiKey);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
