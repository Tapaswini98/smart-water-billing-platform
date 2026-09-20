namespace WaterBilling.Api.Features.Users;

public sealed record CreateUserRequest(
    string Email,
    string Password,
    string FullName,
    string Role,
    string? PhoneNumber = null,
    string? BillingAddress = null);

public sealed record UpdateUserRequest(
    string FullName,
    bool IsActive = true,
    string? PhoneNumber = null,
    string? BillingAddress = null);

public sealed record UserResponse(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    string? PhoneNumber,
    string? BillingAddress,
    bool IsActive,
    int MeterCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

public sealed record UserPageResponse(int Page, int PageSize, int TotalCount, IReadOnlyList<UserResponse> Users);
