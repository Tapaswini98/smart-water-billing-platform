using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Pricing;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Pricing;

public static class PricingEndpoints
{
    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/pricing-plans")
            .WithTags("Pricing")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        group.MapGet("/", ListAsync)
            .WithName("ListPricingPlans")
            .WithSummary("List pricing plans")
            .WithDescription("Every plan with its full version history. Versions are immutable, so this is the complete record of what any meter has ever been billed at.")
            .Produces<IReadOnlyList<PricingPlanResponse>>();

        group.MapGet("/{planId:guid}", GetAsync)
            .WithName("GetPricingPlan")
            .WithSummary("Get a pricing plan")
            .Produces<PricingPlanResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreatePricingPlan")
            .WithSummary("Create a pricing plan")
            .WithDescription("Creates the plan and publishes its first version. Supports fixed per-unit pricing and slab pricing; slab bands are validated for contiguity by the same domain rules the pricing engine uses.")
            .WithValidation<CreatePricingPlanRequest>()
            .Produces<PricingPlanResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{planId:guid}/versions", PublishVersionAsync)
            .WithName("PublishPricingPlanVersion")
            .WithSummary("Publish a new tariff version")
            .WithDescription("Closes the current version and opens a successor. Nothing is edited in place, so an invoice issued under the old rates still reprices identically.")
            .WithValidation<PublishVersionRequest>()
            .Produces<PricingPlanResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{planId:guid}/quote", QuoteAsync)
            .WithName("QuotePricingPlan")
            .WithSummary("Preview a charge")
            .WithDescription("Prices a hypothetical consumption against the plan's current version, returning the full band-by-band breakdown. Lets an admin check a tariff before any customer is billed by it.")
            .WithValidation<PriceQuoteRequest>()
            .Produces<PriceQuoteResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{planId:guid}", DeleteAsync)
            .WithName("DeletePricingPlan")
            .WithSummary("Soft-delete a pricing plan")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Ok<IReadOnlyList<PricingPlanResponse>>> ListAsync(
        WaterBillingDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var plans = await LoadPlansAsync(db, planId: null, cancellationToken);
        var now = timeProvider.GetUtcNow();
        return TypedResults.Ok<IReadOnlyList<PricingPlanResponse>>([.. plans.Select(p => ToResponse(p, now))]);
    }

    private static async Task<Results<Ok<PricingPlanResponse>, ProblemHttpResult>> GetAsync(
        Guid planId,
        WaterBillingDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var plans = await LoadPlansAsync(db, planId, cancellationToken);
        var plan = plans.FirstOrDefault();

        return plan is null
            ? ApiProblems.NotFound($"No pricing plan exists with id {planId}.", "Pricing plan")
            : TypedResults.Ok(ToResponse(plan, timeProvider.GetUtcNow()));
    }

    private static async Task<Results<Created<PricingPlanResponse>, ProblemHttpResult>> CreateAsync(
        CreatePricingPlanRequest request,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();

        if (await db.PricingPlans.AnyAsync(p => p.Name == name, cancellationToken))
        {
            return ApiProblems.Conflict(
                "Plan name already in use",
                $"A pricing plan named '{name}' already exists.",
                "duplicate-plan-name");
        }

        var now = timeProvider.GetUtcNow();
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "INR" : request.Currency.ToUpperInvariant();

        var plan = new PricingPlan
        {
            Name = name,
            Description = request.Description,
            Currency = currency,
            IsDefault = request.IsDefault
        };

        var version = BuildVersion(plan.Id, versionNumber: 1, request.InitialVersion, now);

        // Validate with the domain's own rules before persisting, so an invalid band
        // set is rejected at creation rather than surfacing during a billing run.
        var snapshot = TariffSnapshot.From(version, plan.Name, currency);
        var errors = TariffValidator.Validate(snapshot);

        if (errors.Count > 0)
        {
            return ApiProblems.UnprocessableEntity(
                "Pricing plan is invalid",
                string.Join(" ", errors),
                "invalid-tariff");
        }

        if (request.IsDefault)
        {
            await ClearExistingDefaultAsync(db, cancellationToken);
        }

        plan.Versions.Add(version);
        db.PricingPlans.Add(plan);

        audit.Record("pricing_plan.created", nameof(PricingPlan), plan.Id, new { plan.Name, Mode = version.Mode.ToString() });
        await db.SaveChangesAsync(cancellationToken);

        var created = (await LoadPlansAsync(db, plan.Id, cancellationToken)).First();
        return TypedResults.Created($"/api/v1/pricing-plans/{plan.Id}", ToResponse(created, now));
    }

    private static async Task<Results<Created<PricingPlanResponse>, ProblemHttpResult>> PublishVersionAsync(
        Guid planId,
        PublishVersionRequest request,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var plan = await db.PricingPlans
            .Include(p => p.Versions)
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

        if (plan is null)
        {
            return ApiProblems.NotFound($"No pricing plan exists with id {planId}.", "Pricing plan");
        }

        var now = timeProvider.GetUtcNow();
        var effectiveFrom = request.EffectiveFromUtc ?? now;

        var current = plan.Versions
            .Where(v => v.EffectiveToUtc == null)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        if (current is not null && effectiveFrom <= current.EffectiveFromUtc)
        {
            return ApiProblems.UnprocessableEntity(
                "New version starts before the current one",
                $"Version {current.VersionNumber} takes effect at {current.EffectiveFromUtc:O}. A successor must start after that, or history would have two versions in force at once.",
                "overlapping-tariff-version");
        }

        var nextNumber = plan.Versions.Count == 0 ? 1 : plan.Versions.Max(v => v.VersionNumber) + 1;
        var version = BuildVersion(plan.Id, nextNumber, request, now);
        version.EffectiveFromUtc = effectiveFrom;

        var errors = TariffValidator.Validate(TariffSnapshot.From(version, plan.Name, plan.Currency));

        if (errors.Count > 0)
        {
            return ApiProblems.UnprocessableEntity(
                "Pricing plan version is invalid",
                string.Join(" ", errors),
                "invalid-tariff");
        }

        // Close the outgoing version exactly where the new one starts, so there is
        // never a gap in which no tariff is in force.
        if (current is not null)
        {
            current.EffectiveToUtc = effectiveFrom;
        }

        db.PricingPlanVersions.Add(version);
        audit.Record("pricing_plan.version_published", nameof(PricingPlanVersion), version.Id, new
        {
            PlanId = plan.Id,
            plan.Name,
            Version = nextNumber,
            EffectiveFrom = effectiveFrom
        });

        await db.SaveChangesAsync(cancellationToken);

        var reloaded = (await LoadPlansAsync(db, plan.Id, cancellationToken)).First();
        return TypedResults.Created($"/api/v1/pricing-plans/{plan.Id}", ToResponse(reloaded, now));
    }

    private static async Task<Results<Ok<PriceQuoteResponse>, ProblemHttpResult>> QuoteAsync(
        Guid planId,
        PriceQuoteRequest request,
        TariffResolver resolver,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var tariff = await resolver.ResolveCurrentAsync(planId, now, cancellationToken);

        if (tariff is null)
        {
            return ApiProblems.NotFound(
                $"No pricing plan with id {planId} has a version in force right now.",
                "Pricing plan version");
        }

        var breakdown = PricingEngine.Price(tariff, request.ConsumptionM3);

        return TypedResults.Ok(new PriceQuoteResponse(
            breakdown.PlanId,
            breakdown.PlanName,
            breakdown.PlanVersionNumber,
            breakdown.Currency,
            breakdown.ConsumptionM3,
            [.. breakdown.Lines.Select(l => new PriceLineResponse(
                l.Description, l.FromM3, l.ToM3, l.UnitsM3, l.RatePerM3, l.Amount))],
            breakdown.FixedCharge,
            breakdown.UsageCharge,
            breakdown.Subtotal,
            breakdown.TaxRatePercent,
            breakdown.TaxAmount,
            breakdown.Total));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid planId,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var plan = await db.PricingPlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

        if (plan is null)
        {
            return ApiProblems.NotFound($"No pricing plan exists with id {planId}.", "Pricing plan");
        }

        var metersOnPlan = await db.Meters.CountAsync(m => m.PricingPlanId == planId, cancellationToken);

        if (metersOnPlan > 0)
        {
            return ApiProblems.Conflict(
                "Plan is still in use",
                $"{metersOnPlan} meter(s) are billed on this plan. Move them to another plan first — deleting it would leave them unbillable.",
                "pricing-plan-in-use");
        }

        plan.DeletedAtUtc = timeProvider.GetUtcNow();
        plan.IsDefault = false;

        // Versions are deliberately NOT deleted: historical invoices cite them.
        audit.Record("pricing_plan.deleted", nameof(PricingPlan), plan.Id, new { plan.Name });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task ClearExistingDefaultAsync(WaterBillingDbContext db, CancellationToken cancellationToken)
    {
        // A filtered unique index enforces at most one default in the database; this
        // keeps the API from tripping over it with a confusing constraint error.
        var existing = await db.PricingPlans.Where(p => p.IsDefault).ToListAsync(cancellationToken);
        foreach (var plan in existing)
        {
            plan.IsDefault = false;
        }
    }

    private static PricingPlanVersion BuildVersion(
        Guid planId,
        int versionNumber,
        PublishVersionRequest request,
        DateTimeOffset now)
    {
        var versionId = Guid.CreateVersion7();
        var mode = Enum.Parse<PricingMode>(request.Mode, ignoreCase: true);

        var version = new PricingPlanVersion
        {
            Id = versionId,
            PricingPlanId = planId,
            VersionNumber = versionNumber,
            Mode = mode,
            SlabMode = request.SlabMode is null
                ? SlabMode.Progressive
                : Enum.Parse<SlabMode>(request.SlabMode, ignoreCase: true),
            FixedCharge = request.FixedCharge,
            RatePerM3 = request.RatePerM3,
            TaxRatePercent = request.TaxRatePercent,
            EffectiveFromUtc = request.EffectiveFromUtc ?? now
        };

        if (mode is PricingMode.Slab)
        {
            var order = 1;
            // Slabs are optional on the wire (a flat-rate plan sends none), so null
            // is a valid absence rather than a caller error.
            foreach (var slab in (request.Slabs ?? []).OrderBy(s => s.FromM3))
            {
                version.Slabs.Add(new PricingSlab
                {
                    PricingPlanVersionId = versionId,
                    SortOrder = order++,
                    FromM3 = slab.FromM3,
                    ToM3 = slab.ToM3,
                    RatePerM3 = slab.RatePerM3
                });
            }
        }

        return version;
    }

    private static async Task<List<PricingPlan>> LoadPlansAsync(
        WaterBillingDbContext db,
        Guid? planId,
        CancellationToken cancellationToken)
    {
        var query = db.PricingPlans
            .AsNoTracking()
            .Include(p => p.Versions.OrderBy(v => v.VersionNumber))
            .ThenInclude(v => v.Slabs.OrderBy(s => s.SortOrder))
            .AsQueryable();

        if (planId is { } id)
        {
            query = query.Where(p => p.Id == id);
        }

        return await query.OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    private static PricingPlanResponse ToResponse(PricingPlan plan, DateTimeOffset now) => new(
        plan.Id,
        plan.Name,
        plan.Description,
        plan.Currency,
        plan.IsDefault,
        plan.Meters.Count,
        [.. plan.Versions
            .OrderBy(v => v.VersionNumber)
            .Select(v => new PricingPlanVersionResponse(
                v.Id,
                v.VersionNumber,
                v.Mode.ToString(),
                v.SlabMode.ToString(),
                v.FixedCharge,
                v.RatePerM3,
                v.TaxRatePercent,
                v.EffectiveFromUtc,
                v.EffectiveToUtc,
                v.IsEffectiveAt(now),
                [.. v.Slabs
                    .OrderBy(s => s.SortOrder)
                    .Select(s => new SlabResponse(s.SortOrder, s.FromM3, s.ToM3, s.RatePerM3))]))]);
}
