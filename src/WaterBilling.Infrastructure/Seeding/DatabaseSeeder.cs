using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WaterBilling.Domain.Meters;
using WaterBilling.Domain.Pricing;
using WaterBilling.Domain.Readings;
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
        await SeedReadingsAsync(now, cancellationToken);

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

    /// <summary>
    /// Three months of readings ending at "now", so a billing run for the previous
    /// calendar month has a complete series to work from the moment the stack starts.
    /// </summary>
    private async Task SeedReadingsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await db.MeterReadings.AnyAsync(cancellationToken))
        {
            return;
        }

        var from = now.AddMonths(-3);
        var totalInserted = 0;

        foreach (var (id, serial, customerIndex, _, _) in DemoSeedData.Meters)
        {
            if (customerIndex < 0)
            {
                // The unassigned spare stays silent: a billing run needs something to
                // report as skipped, and an uncommissioned meter is exactly that.
                continue;
            }

            var index = Array.FindIndex(DemoSeedData.Meters, m => m.Id == id);

            var plan = new ReadingGenerator.Plan(
                MeterId: id,
                StartingTotalM3: 1000m + (index * 250m),
                // Spread across the slab bands so the seeded invoices are not all in
                // band one — the breakdown on the invoice is then worth looking at.
                DailyAverageM3: 0.35 + (index * 0.28),
                PlantReset: index == 0,
                PlantGap: index == 2,
                PlantContinuousFlow: index == 4);

            var readings = ReadingGenerator.Generate(plan, from, now, seed: 20260919 + index);
            ReadingGenerator.PlantOutOfOrderArrival(readings);

            foreach (var reading in readings)
            {
                reading.CreatedAtUtc = reading.ReceivedAtUtc;
            }

            db.MeterReadings.AddRange(readings);
            totalInserted += readings.Count;

            // A register reset is an operational event, not just a flag on a row:
            // billing has to know to sum across it.
            var resetReading = readings.FirstOrDefault(r => r.Anomalies.HasFlag(ReadingAnomaly.TotalDecreased));
            if (resetReading is not null)
            {
                var previous = readings
                    .Where(r => r.ReadingAtUtc < resetReading.ReadingAtUtc)
                    .OrderByDescending(r => r.ReadingAtUtc)
                    .First();

                db.MeterResetEvents.Add(new MeterResetEvent
                {
                    MeterId = id,
                    DetectedAtUtc = resetReading.ReadingAtUtc,
                    PreviousTotalM3 = previous.TotalM3,
                    NewTotalM3 = resetReading.TotalM3,
                    TriggeringReadingId = resetReading.Id,
                    Notes = "Seeded meter replacement.",
                    CreatedAtUtc = resetReading.ReadingAtUtc
                });
            }

            logger.LogInformation("Seeded {Count} readings for meter {Serial}.", readings.Count, serial);
        }

        // Batched once rather than per meter: ~50k rows in a single transaction is
        // still well within what a dev-box PostgreSQL absorbs comfortably.
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Total} meter readings across three months, including a planted " +
            "out-of-order arrival, a register reset, a 30-hour reporting gap and a " +
            "continuous-flow leak candidate. (A duplicate cannot be seeded — the unique " +
            "index rejects it; POST the same reading twice to see that path.)",
            totalInserted);
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
