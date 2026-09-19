using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WaterBilling.Api.Infrastructure;
using WaterBilling.Infrastructure.Auth;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Authentication");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingPolicies.Authentication)
            .WithName("Login")
            .WithSummary("Exchange credentials for a bearer token")
            .WithDescription(
                "Returns a JWT carrying the user's role. Admin and Customer are distinguished by the " +
                "role claim; meters do not log in here — they authenticate per-request with X-Meter-Key.")
            .WithValidation<LoginRequest>()
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        WaterBillingDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Authentication");
        var email = request.Email.Trim().ToLowerInvariant();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        // One message and one code path for "no such user" and "wrong password".
        // Distinguishing them turns the login endpoint into an account enumeration
        // oracle, and the extra detail helps an attacker far more than a user.
        if (user is null || !user.IsActive || !passwordHasher.Verify(user.PasswordHash, request.Password, out var rehashNeeded))
        {
            logger.LogWarning("Failed login attempt for {Email}.", email);

            return TypedResults.Problem(
                title: "Invalid credentials",
                detail: "The email address or password is incorrect.",
                statusCode: StatusCodes.Status401Unauthorized,
                type: ApiProblems.BaseUri + "invalid-credentials");
        }

        if (rehashNeeded)
        {
            // Transparent upgrade when the hashing parameters have moved on.
            user.PasswordHash = passwordHasher.Hash(request.Password);
        }

        user.LastLoginAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        var token = tokenService.Issue(user);

        return TypedResults.Ok(new LoginResponse(
            token.Token,
            token.TokenType,
            token.ExpiresAtUtc,
            user.Id,
            user.Email,
            user.FullName,
            user.Role.ToString()));
    }
}
