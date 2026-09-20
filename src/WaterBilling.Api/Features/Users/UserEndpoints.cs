using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Domain.Users;
using WaterBilling.Infrastructure.Auth;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Users;

public static class UserEndpoints
{
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        group.MapPost("/", CreateAsync)
            .WithName("CreateUser")
            .WithSummary("Create a user")
            .WithDescription("Admin only. Creates an Admin or Customer account. Passwords are hashed with PBKDF2-HMAC-SHA512 and never stored or returned in clear.")
            .WithValidation<CreateUserRequest>()
            .Produces<UserResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", ListAsync)
            .WithName("ListUsers")
            .WithSummary("List users")
            .Produces<UserPageResponse>();

        group.MapGet("/{userId:guid}", GetAsync)
            .WithName("GetUser")
            .WithSummary("Get a user")
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{userId:guid}", UpdateAsync)
            .WithName("UpdateUser")
            .WithSummary("Update a user's profile")
            .WithValidation<UpdateUserRequest>()
            .Produces<UserResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{userId:guid}", DeleteAsync)
            .WithName("DeleteUser")
            .WithSummary("Soft-delete a user")
            .WithDescription("The row is retained with a deletion timestamp so existing invoices stay attributable. Meters held by the user are unassigned rather than deleted, and the email address becomes reusable.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Results<Created<UserResponse>, ProblemHttpResult>> CreateAsync(
        CreateUserRequest request,
        WaterBillingDbContext db,
        IPasswordHasher passwordHasher,
        AuditService audit,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return ApiProblems.Conflict(
                "Email already registered",
                $"An active account already exists for {email}.",
                "duplicate-email");
        }

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = Enum.Parse<UserRole>(request.Role, ignoreCase: true),
            PhoneNumber = request.PhoneNumber,
            BillingAddress = request.BillingAddress
        };

        db.Users.Add(user);
        // Never log the password or its hash — the audit trail records that an
        // account was created, not the material needed to impersonate it.
        audit.Record("user.created", nameof(User), user.Id, new { user.Email, Role = user.Role.ToString() });

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/v1/users/{user.Id}", ToResponse(user, meterCount: 0));
    }

    private static async Task<Ok<UserPageResponse>> ListAsync(
        WaterBillingDbContext db,
        CancellationToken cancellationToken,
        string? role = null,
        string? search = null,
        int page = 1,
        int pageSize = 50)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        page = Math.Max(page, 1);

        var query = db.Users.AsNoTracking();

        if (role is not null && Enum.TryParse<UserRole>(role, ignoreCase: true, out var parsedRole))
        {
            query = query.Where(u => u.Role == parsedRole);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);

        var users = await query
            .OrderBy(u => u.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserResponse(
                u.Id,
                u.Email,
                u.FullName,
                u.Role.ToString(),
                u.PhoneNumber,
                u.BillingAddress,
                u.IsActive,
                u.Meters.Count(m => m.DeletedAtUtc == null),
                u.CreatedAtUtc,
                u.LastLoginAtUtc))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new UserPageResponse(page, pageSize, total, users));
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> GetAsync(
        Guid userId,
        WaterBillingDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new UserResponse(
                u.Id, u.Email, u.FullName, u.Role.ToString(), u.PhoneNumber, u.BillingAddress,
                u.IsActive, u.Meters.Count(m => m.DeletedAtUtc == null), u.CreatedAtUtc, u.LastLoginAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        return user is null
            ? ApiProblems.NotFound($"No user exists with id {userId}.", "User")
            : TypedResults.Ok(user);
    }

    private static async Task<Results<Ok<UserResponse>, ProblemHttpResult>> UpdateAsync(
        Guid userId,
        UpdateUserRequest request,
        WaterBillingDbContext db,
        AuditService audit,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return ApiProblems.NotFound($"No user exists with id {userId}.", "User");
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber;
        user.BillingAddress = request.BillingAddress;
        user.IsActive = request.IsActive;

        audit.Record("user.updated", nameof(User), user.Id, new { user.FullName, user.IsActive });
        await db.SaveChangesAsync(cancellationToken);

        var meterCount = await db.Meters.CountAsync(m => m.CustomerId == user.Id, cancellationToken);
        return TypedResults.Ok(ToResponse(user, meterCount));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid userId,
        WaterBillingDbContext db,
        AuditService audit,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return ApiProblems.NotFound($"No user exists with id {userId}.", "User");
        }

        // Losing the last administrator locks everyone out of the system with no
        // recovery path short of editing the database by hand.
        if (user.Role is UserRole.Admin)
        {
            var remainingAdmins = await db.Users.CountAsync(
                u => u.Role == UserRole.Admin && u.Id != userId && u.IsActive,
                cancellationToken);

            if (remainingAdmins == 0)
            {
                return ApiProblems.Conflict(
                    "Cannot delete the last administrator",
                    "At least one active administrator must remain. Create another admin account first.",
                    "last-administrator");
            }
        }

        var now = timeProvider.GetUtcNow();
        user.DeletedAtUtc = now;
        user.IsActive = false;

        // Meters are retained and unassigned: their readings and invoices are
        // financial history and must stay intact (ADR-0010).
        var meters = await db.Meters.Where(m => m.CustomerId == userId).ToListAsync(cancellationToken);
        foreach (var meter in meters)
        {
            meter.CustomerId = null;
        }

        audit.Record("user.deleted", nameof(User), user.Id, new { user.Email, UnassignedMeters = meters.Count });
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.NoContent();
    }

    private static UserResponse ToResponse(User user, int meterCount) => new(
        user.Id,
        user.Email,
        user.FullName,
        user.Role.ToString(),
        user.PhoneNumber,
        user.BillingAddress,
        user.IsActive,
        meterCount,
        user.CreatedAtUtc,
        user.LastLoginAtUtc);
}
