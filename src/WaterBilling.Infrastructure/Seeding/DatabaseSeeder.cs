using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaterBilling.Domain.Meters;
using WaterBilling.Domain.Pricing;
using WaterBilling.Domain.Users;
using WaterBilling.Infrastructure.Auth;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Infrastructure.Seeding;

/// <summary>
/// Populates a Development database with a coherent demo estate: users, meters,
/// per-meter ingestion keys, and one plan of each pricing shape.
/// <para>
/// Idempotent — it checks before it inserts — so restarting the stack does not
/// duplicate anything, and a reviewer can run <c>docker compose up</c> twice
/// without thinking about it.
/// </para>
/// </summary>
public sealed class DatabaseSeeder(
    WaterBillingDbContext db,
    IPasswordHasher passwordHasher,
    IOptions<SeedOptions> options,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<DatabaseSeeder> logger)
{
    private readonly SeedOptions options = options.Value;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Seeding is disabled (Seed:Enabled = false). Skipping.");
            return;
        }

        if (!environment.IsDevelopment())
        {
            // Belt and braces alongside the config flag: seed data carries known
            // passwords and known API keys, and must never exist outside a dev box.
            logger.LogWarning(
                "Seeding was requested in the {Environment} environment and has been refused. " +
                "Demo data contains well-known credentials and is Development-only.",
                environment.EnvironmentName);
            return;
        }

        var now = timeProvider.GetUtcNow();

        await SeedUsersAsync(now, cancellationToken);
        await SeedPricingPlansAsync(now, cancellationToken);
        await SeedMetersAsync(now, cancellationToken);

        logger.LogInformation(
            "Demo data ready. Admin login: {AdminEmail} / {AdminPassword}. Customer logins use {CustomerPassword}.",
            options.AdminEmail, options.AdminPassword, options.CustomerPassword);
    }

    private async Task SeedUsersAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        db.Users.Add(new User
        {
            Id = DemoSeedData.AdminUserId,
            Email = options.AdminEmail.ToLowerInvariant(),
            FullName = "Platform Administrator",
            PasswordHash = passwordHasher.Hash(options.AdminPassword),
            Role = UserRole.Admin,
            CreatedAtUtc = now
        });

        foreach (var (id, email, fullName, address) in DemoSeedData.Customers)
        {
            db.Users.Add(new User
            {
                Id = id,
                Email = email,
                FullName = fullName,
                BillingAddress = address,
                PasswordHash = passwordHasher.Hash(options.CustomerPassword),
                Role = UserRole.Customer,
                CreatedAtUtc = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded 1 admin and {Count} customers.", DemoSeedData.Customers.Length);
    }

    private async Task SeedPricingPlansAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.PricingPlans.AnyAsync(cancellationToken))
        {
            return;
        }

        // Effective from well before any demo reading, so that every seeded period
        // resolves to a tariff version rather than falling off the start of history.
        var effectiveFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        db.PricingPlans.Add(new PricingPlan
        {
            Id = DemoSeedData.FlatRatePlanId,
            Name = "Commercial Flat Rate",
            Description = "A single rate per m3 plus a standing charge. Used for commercial connections.",
            Currency = "INR",
            CreatedAtUtc = now,
            Versions =
            [
                new PricingPlanVersion
                {
                    PricingPlanId = DemoSeedData.FlatRatePlanId,
                    VersionNumber = 1,
                    Mode = PricingMode.FlatRate,
                    SlabMode = SlabMode.Progressive,
                    FixedCharge = 150.00m,
                    RatePerM3 = 32.0000m,
                    TaxRatePercent = 18.000m,
                    EffectiveFromUtc = effectiveFrom,
                    CreatedAtUtc = now
                }
            ]
        });

        var slabVersionId = Guid.CreateVersion7();

        db.PricingPlans.Add(new PricingPlan
        {
            Id = DemoSeedData.SlabPlanId,
            Name = "Domestic Progressive Slab",
            Description = "Telescopic bands that rise with consumption, to discourage heavy use.",
            Currency = "INR",
            IsDefault = true,
            CreatedAtUtc = now,
            Versions =
            [
                new PricingPlanVersion
                {
                    Id = slabVersionId,
                    PricingPlanId = DemoSeedData.SlabPlanId,
                    VersionNumber = 1,
                    Mode = PricingMode.Slab,
                    SlabMode = SlabMode.Progressive,
                    FixedCharge = 100.00m,
                    TaxRatePercent = 18.000m,
                    EffectiveFromUtc = effectiveFrom,
                    CreatedAtUtc = now,
                    Slabs =
                    [
                        new PricingSlab { PricingPlanVersionId = slabVersionId, SortOrder = 1, FromM3 = 0m, ToM3 = 10m, RatePerM3 = 12.0000m, CreatedAtUtc = now },
                        new PricingSlab { PricingPlanVersionId = slabVersionId, SortOrder = 2, FromM3 = 10m, ToM3 = 25m, RatePerM3 = 22.0000m, CreatedAtUtc = now },
                        new PricingSlab { PricingPlanVersionId = slabVersionId, SortOrder = 3, FromM3 = 25m, ToM3 = 50m, RatePerM3 = 38.0000m, CreatedAtUtc = now },
                        new PricingSlab { PricingPlanVersionId = slabVersionId, SortOrder = 4, FromM3 = 50m, ToM3 = null, RatePerM3 = 60.0000m, CreatedAtUtc = now }
                    ]
                }
            ]
        });

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded 2 pricing plans (one flat rate, one progressive slab).");
    }

    private async Task SeedMetersAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.Meters.AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var (id, serial, customerIndex, location, apiKey) in DemoSeedData.Meters)
        {
            var isCommercial = customerIndex == 1;

            db.Meters.Add(new Meter
            {
                Id = id,
                SerialNumber = serial,
                Model = "AquaSense 200",
                LocationDescription = location,
                InstalledAtUtc = now.AddYears(-1),
                Status = customerIndex >= 0 ? MeterStatus.Active : MeterStatus.Provisioned,
                CustomerId = customerIndex >= 0 ? DemoSeedData.Customers[customerIndex].Id : null,
                PricingPlanId = isCommercial ? DemoSeedData.FlatRatePlanId : DemoSeedData.SlabPlanId,
                CreatedAtUtc = now,
                ApiKeys =
                [
                    new MeterApiKey
                    {
                        MeterId = id,
                        Prefix = apiKey[..MeterApiKeyGenerator.PrefixLength],
                        KeyHash = MeterApiKeyGenerator.Hash(apiKey),
                        Label = "Demo key (Development only)",
                        CreatedAtUtc = now
                    }
                ]
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Count} meters with demo ingestion keys. Example: meter {Serial} uses X-Meter-Key: {Key}",
            DemoSeedData.Meters.Length,
            DemoSeedData.Meters[0].Serial,
            DemoSeedData.Meters[0].ApiKey);
    }
}
