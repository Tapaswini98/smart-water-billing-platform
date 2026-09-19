using Microsoft.AspNetCore.Identity;
using WaterBilling.Domain.Users;

namespace WaterBilling.Infrastructure.Auth;

/// <summary>
/// Wraps ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> — PBKDF2-HMAC-SHA512,
/// 210,000 iterations, per-password salt, constant-time comparison, with a version
/// byte that makes future parameter upgrades a non-event.
/// <para>
/// Deliberately not a hand-rolled implementation and deliberately not plain SHA-256:
/// password hashing is the one place in this codebase where writing it yourself has
/// no upside.
/// </para>
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> inner = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return inner.HashPassword(placeholder, password);
    }

    public bool Verify(string hash, string password, out bool rehashNeeded)
    {
        rehashNeeded = false;

        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var result = inner.VerifyHashedPassword(placeholder, hash, password);

        switch (result)
        {
            case PasswordVerificationResult.Success:
                return true;
            case PasswordVerificationResult.SuccessRehashNeeded:
                rehashNeeded = true;
                return true;
            default:
                return false;
        }
    }

    // Identity's hasher takes a user only to satisfy its generic signature; it never
    // reads it. One shared instance avoids allocating a throwaway per call.
    private static readonly User placeholder = new()
    {
        Email = string.Empty,
        PasswordHash = string.Empty,
        FullName = string.Empty
    };
}
