using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Meters;
using WaterBilling.Domain.Users;
using WaterBilling.Infrastructure.Auth;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Meters;

public static class MeterEndpoints
{
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/meters").WithTags("Meters");

        // Customers may list meters — the query is scoped to their own.
        group.MapGet("/", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedUser)
            .WithName("ListMeters")
            .WithSummary("List meters")
            .WithDescription("Admins see every meter. A customer sees only meters assigned to their own account; the filter is applied server-side, not by the caller.")
            .Produces<MeterPageResponse>();

        group.MapGet("/{meterId:guid}", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.AuthenticatedUser)
            .WithName("GetMeter")
            .WithSummary("Get a meter")
            .Produces<MeterResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var admin = group.MapGroup("/").RequireAuthorization(AuthorizationPolicies.AdminOnly);

        admin.MapPost("/", CreateAsync)
            .WithName("CreateMeter")
            .WithSummary("Create a water meter")
            .WithDescription("Admin only. Issues the meter's first ingestion API key in the same response — the only time the plaintext key is ever shown.")
            .WithValidation<CreateMeterRequest>()
            .Produces<MeterResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        admin.MapPut("/{meterId:guid}", UpdateAsync)
            .WithName("UpdateMeter")
            .WithSummary("Update a meter")
            .WithValidation<UpdateMeterRequest>()
            .Produces<MeterResponse>();

        admin.MapPut("/{meterId:guid}/assignment", AssignAsync)
            .WithName("AssignMeter")
            .WithSummary("Assign or unassign a meter")
            .WithDescription("Sets the customer billed for this meter. Passing null unassigns it; already-issued invoices keep the customer they were issued to.")
            .Produces<MeterResponse>();

        admin.MapDelete("/{meterId:guid}", DeleteAsync)
            .WithName("DeleteMeter")
            .WithSummary("Soft-delete a meter")
            .Produces(StatusCodes.Status204NoContent);

        admin.MapPost("/{meterId:guid}/api-keys", IssueApiKeyAsync)
            .WithName("IssueMeterApiKey")
            .WithSummary("Issue an ingestion key")
            .WithValidation<IssueApiKeyRequest>()
            .Produces<IssuedApiKeyResponse>(StatusCodes.Status201Created);

        admin.MapGet("/{meterId:guid}/api-keys", ListApiKeysAsync)
            .WithName("ListMeterApiKeys")
            .WithSummary("List a meter's keys")
            .WithDescription("Returns prefixes and lifecycle timestamps only. Plaintext keys are not recoverable by design.")
            .Produces<IReadOnlyList<ApiKeySummaryResponse>>();

        admin.MapDelete("/{meterId:guid}/api-keys/{keyId:guid}", RevokeApiKeyAsync)
            .WithName("RevokeMeterApiKey")
            .WithSummary("Revoke an ingestion key")
            .Produces(StatusCodes.Status204NoContent);

        admin.MapPut("/{meterId:guid}/supply", SetSupplyStateAsync)
            .WithName("SetMeterSupplyState")
            .WithSummary("Open or close the supply valve")
            .WithDescription("Records the DESIRED valve position. The on-site gateway reconciles the physical relay and reports back, so a valve that fails to actuate stays visible instead of being assumed closed.")
            .WithValidation<SupplyStateRequest>()
            .Produces<MeterResponse>();

        return app;
    }

    private static async Task<Results<Created<IssuedApiKeyResponse>, ProblemHttpResult>> CreateAsync(
        CreateMeterRequest request,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var serial = request.SerialNumber.Trim();

        if (await db.Meters.AnyAsync(m => m.SerialNumber == serial, cancellationToken))
        {
            return ApiProblems.Conflict(
                "Serial number already registered",
                $"An active meter already exists with serial number {serial}.",
                "duplicate-meter-serial");
        }

        if (request.CustomerId is { } customerId)
        {
            var customer = await db.Users
                .Where(u => u.Id == customerId)
                .Select(u => new { u.Role })
                .FirstOrDefaultAsync(cancellationToken);

            if (customer is null)
            {
                return ApiProblems.UnprocessableEntity(
                    "Customer not found",
                    $"No active user exists with id {customerId}.",
                    "unknown-customer");
            }

            if (customer.Role is not UserRole.Customer)
            {
                return ApiProblems.UnprocessableEntity(
                    "Meters can only be assigned to customers",
                    "The specified user is an administrator. Meters are billed to customer accounts.",
                    "invalid-meter-assignment");
            }
        }

        if (request.PricingPlanId is { } planId
            && !await db.PricingPlans.AnyAsync(p => p.Id == planId, cancellationToken))
        {
            return ApiProblems.UnprocessableEntity(
                "Pricing plan not found",
                $"No pricing plan exists with id {planId}.",
                "unknown-pricing-plan");
        }

        var now = timeProvider.GetUtcNow();

        var meter = new Meter
        {
            SerialNumber = serial,
            Model = request.Model,
            LocationDescription = request.LocationDescription,
            InstalledAtUtc = request.InstalledAtUtc ?? now,
            // A meter with a customer is ready to bill; one without is still being
            // commissioned. Defaulting this saves an extra call in the common case.
            Status = request.CustomerId is null ? MeterStatus.Provisioned : MeterStatus.Active,
            CustomerId = request.CustomerId,
            PricingPlanId = request.PricingPlanId
        };

        var (plainTextKey, prefix, hash) = MeterApiKeyGenerator.Generate();

        meter.ApiKeys.Add(new MeterApiKey
        {
            MeterId = meter.Id,
            Prefix = prefix,
            KeyHash = hash,
            Label = "Issued at commissioning"
        });

        db.Meters.Add(meter);
        // The prefix, never the key: an audit log that contains working credentials
        // is a credential store with extra steps.
        audit.Record("meter.created", nameof(Meter), meter.Id, new { meter.SerialNumber, KeyPrefix = prefix });

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/v1/meters/{meter.Id}",
            new IssuedApiKeyResponse(
                meter.ApiKeys.First().Id,
                meter.Id,
                meter.SerialNumber,
                plainTextKey,
                prefix,
                MeterApiKeyGenerator.HeaderName,
                null,
                "Store this key now. It is hashed at rest and cannot be shown again — issue a new key if it is lost."));
    }

    private static async Task<Ok<MeterPageResponse>> ListAsync(
        ClaimsPrincipal principal,
        WaterBillingDbContext db,
        CancellationToken cancellationToken,
        Guid? customerId = null,
        string? status = null,
        string? search = null,
        int page = 1,
        int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.Meters.AsNoTracking();

        // Authorization by construction: a customer's query is narrowed before any
        // filter they supplied is applied, so a customerId parameter cannot widen it.
        if (!principal.IsAdmin())
        {
            var callerId = principal.GetUserId();
            query = query.Where(m => m.CustomerId == callerId);
        }
        else if (customerId is { } filterCustomerId)
        {
            query = query.Where(m => m.CustomerId == filterCustomerId);
        }

        if (status is not null && Enum.TryParse<MeterStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(m => m.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(m =>
                m.SerialNumber.ToLower().Contains(term) ||
                (m.LocationDescription != null && m.LocationDescription.ToLower().Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);

        var meters = await query
            .OrderBy(m => m.SerialNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(Project)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new MeterPageResponse(page, pageSize, total, meters));
    }

    private static async Task<Results<Ok<MeterResponse>, ProblemHttpResult>> GetAsync(
        Guid meterId,
        ClaimsPrincipal principal,
        WaterBillingDbContext db,
        CancellationToken cancellationToken)
    {
        // Ownership is checked by the shared helper, so this route cannot drift from
        // the rule the consumption and invoice routes apply.
        var access = await MeterAccess.CheckAsync(db, meterId, principal, cancellationToken);

        if (access.Problem is { } problem)
        {
            return problem;
        }

        return await ReloadAsync(meterId, db, cancellationToken);
    }

    private static async Task<Results<Ok<MeterResponse>, ProblemHttpResult>> UpdateAsync(
        Guid meterId,
        UpdateMeterRequest request,
        WaterBillingDbContext db,
        AuditService audit,
        CancellationToken cancellationToken)
    {
        var meter = await db.Meters.FirstOrDefaultAsync(m => m.Id == meterId, cancellationToken);

        if (meter is null)
        {
            return ApiProblems.NotFound($"No meter exists with id {meterId}.", "Meter");
        }

        if (request.PricingPlanId is { } planId
            && !await db.PricingPlans.AnyAsync(p => p.Id == planId, cancellationToken))
        {
            return ApiProblems.UnprocessableEntity(
                "Pricing plan not found",
                $"No pricing plan exists with id {planId}.",
                "unknown-pricing-plan");
        }

        meter.Model = request.Model;
        meter.LocationDescription = request.LocationDescription;
        meter.Status = Enum.Parse<MeterStatus>(request.Status, ignoreCase: true);
        meter.PricingPlanId = request.PricingPlanId;

        audit.Record("meter.updated", nameof(Meter), meter.Id, new { Status = meter.Status.ToString(), meter.PricingPlanId });
        await db.SaveChangesAsync(cancellationToken);

        return await ReloadAsync(meterId, db, cancellationToken);
    }

    private static async Task<Results<Ok<MeterResponse>, ProblemHttpResult>> AssignAsync(
        Guid meterId,
        AssignMeterRequest request,
        WaterBillingDbContext db,
        AuditService audit,
        CancellationToken cancellationToken)
    {
        var meter = await db.Meters.FirstOrDefaultAsync(m => m.Id == meterId, cancellationToken);

        if (meter is null)
        {
            return ApiProblems.NotFound($"No meter exists with id {meterId}.", "Meter");
        }

        if (request.CustomerId is { } customerId)
        {
            var role = await db.Users
                .Where(u => u.Id == customerId)
                .Select(u => (UserRole?)u.Role)
                .FirstOrDefaultAsync(cancellationToken);

            if (role is null)
            {
                return ApiProblems.UnprocessableEntity(
                    "Customer not found",
                    $"No active user exists with id {customerId}.",
                    "unknown-customer");
            }

            if (role is not UserRole.Customer)
            {
                return ApiProblems.UnprocessableEntity(
                    "Meters can only be assigned to customers",
                    "The specified user is an administrator.",
                    "invalid-meter-assignment");
            }
        }

        var previousCustomerId = meter.CustomerId;
        meter.CustomerId = request.CustomerId;

        // Assigning a meter commissions it; unassigning does not decommission it,
        // because a vacant property's meter should keep measuring.
        if (request.CustomerId is not null && meter.Status is MeterStatus.Provisioned)
        {
            meter.Status = MeterStatus.Active;
        }

        audit.Record("meter.assigned", nameof(Meter), meter.Id, new { From = previousCustomerId, To = request.CustomerId });
        await db.SaveChangesAsync(cancellationToken);

        return await ReloadAsync(meterId, db, cancellationToken);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid meterId,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var meter = await db.Meters.FirstOrDefaultAsync(m => m.Id == meterId, cancellationToken);

        if (meter is null)
        {
            return ApiProblems.NotFound($"No meter exists with id {meterId}.", "Meter");
        }

        meter.DeletedAtUtc = timeProvider.GetUtcNow();
        meter.Status = MeterStatus.Decommissioned;

        // Readings and invoices are deliberately untouched: they are the evidence
        // behind bills that have already been issued (ADR-0010).
        audit.Record("meter.deleted", nameof(Meter), meter.Id, new { meter.SerialNumber });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Created<IssuedApiKeyResponse>, ProblemHttpResult>> IssueApiKeyAsync(
        Guid meterId,
        IssueApiKeyRequest request,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var meter = await db.Meters
            .Where(m => m.Id == meterId)
            .Select(m => new { m.Id, m.SerialNumber })
            .FirstOrDefaultAsync(cancellationToken);

        if (meter is null)
        {
            return ApiProblems.NotFound($"No meter exists with id {meterId}.", "Meter");
        }

        var (plainTextKey, prefix, hash) = MeterApiKeyGenerator.Generate();
        var expiresAt = request.ExpiresInDays is { } days
            ? timeProvider.GetUtcNow().AddDays(days)
            : (DateTimeOffset?)null;

        var key = new MeterApiKey
        {
            MeterId = meter.Id,
            Prefix = prefix,
            KeyHash = hash,
            Label = request.Label,
            ExpiresAtUtc = expiresAt
        };

        db.MeterApiKeys.Add(key);
        audit.Record("meter.api_key_issued", nameof(MeterApiKey), key.Id, new { MeterId = meter.Id, Prefix = prefix });

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/v1/meters/{meter.Id}/api-keys/{key.Id}",
            new IssuedApiKeyResponse(
                key.Id, meter.Id, meter.SerialNumber, plainTextKey, prefix,
                MeterApiKeyGenerator.HeaderName, expiresAt,
                "Store this key now. It is hashed at rest and cannot be shown again — issue a new key if it is lost."));
    }

    private static async Task<Ok<IReadOnlyList<ApiKeySummaryResponse>>> ListApiKeysAsync(
        Guid meterId,
        WaterBillingDbContext db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var keys = await db.MeterApiKeys
            .AsNoTracking()
            .Where(k => k.MeterId == meterId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .Select(k => new ApiKeySummaryResponse(
                k.Id,
                k.Prefix,
                k.Label,
                k.CreatedAtUtc,
                k.ExpiresAtUtc,
                k.RevokedAtUtc,
                k.LastUsedAtUtc,
                k.RevokedAtUtc == null && (k.ExpiresAtUtc == null || k.ExpiresAtUtc > now)))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<ApiKeySummaryResponse>>(keys);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeApiKeyAsync(
        Guid meterId,
        Guid keyId,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var key = await db.MeterApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.MeterId == meterId, cancellationToken);

        if (key is null)
        {
            return ApiProblems.NotFound($"No key with id {keyId} exists for meter {meterId}.", "API key");
        }

        key.RevokedAtUtc ??= timeProvider.GetUtcNow();
        audit.Record("meter.api_key_revoked", nameof(MeterApiKey), key.Id, new { MeterId = meterId, key.Prefix });

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<MeterResponse>, ProblemHttpResult>> SetSupplyStateAsync(
        Guid meterId,
        SupplyStateRequest request,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var meter = await db.Meters.FirstOrDefaultAsync(m => m.Id == meterId, cancellationToken);

        if (meter is null)
        {
            return ApiProblems.NotFound($"No meter exists with id {meterId}.", "Meter");
        }

        var desired = Enum.Parse<SupplyState>(request.DesiredState, ignoreCase: true);

        meter.DesiredSupplyState = desired;
        meter.SupplyStateReason = request.Reason;
        meter.SupplyStateChangedAtUtc = timeProvider.GetUtcNow();

        audit.Record("supply.state_requested", nameof(Meter), meter.Id, new
        {
            meter.SerialNumber,
            Desired = desired.ToString(),
            request.Reason
        });

        await db.SaveChangesAsync(cancellationToken);

        return await ReloadAsync(meterId, db, cancellationToken);
    }

    private static async Task<Results<Ok<MeterResponse>, ProblemHttpResult>> ReloadAsync(
        Guid meterId,
        WaterBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var response = await db.Meters
            .AsNoTracking()
            .Where(m => m.Id == meterId)
            .Select(Project)
            .FirstOrDefaultAsync(cancellationToken);

        return response is null
            ? ApiProblems.NotFound($"No meter exists with id {meterId}.", "Meter")
            : TypedResults.Ok(response);
    }

    /// <summary>
    /// Shared projection, written once so the list and detail responses cannot drift.
    /// <para>
    /// An <see cref="Expression"/> field rather than a method: a method call inside a
    /// Select is client-evaluated, which materialises whole entities and leaves every
    /// navigation null unless separately Included. As an expression tree EF translates
    /// it into a single SQL statement with the joins it needs.
    /// </para>
    /// </summary>
    private static readonly Expression<Func<Meter, MeterResponse>> Project = m => new MeterResponse(
        m.Id,
        m.SerialNumber,
        m.Model,
        m.LocationDescription,
        m.InstalledAtUtc,
        m.Status.ToString(),
        m.CustomerId,
        m.Customer == null ? null : m.Customer.FullName,
        m.PricingPlanId,
        m.PricingPlan == null ? null : m.PricingPlan.Name,
        m.DesiredSupplyState.ToString(),
        m.ReportedSupplyState == null ? null : m.ReportedSupplyState.ToString(),
        m.SupplyStateReason,
        m.Readings.Max(r => (DateTimeOffset?)r.ReadingAtUtc),
        m.Readings
            .OrderByDescending(r => r.ReadingAtUtc)
            .Select(r => (decimal?)r.TotalM3)
            .FirstOrDefault());
}
