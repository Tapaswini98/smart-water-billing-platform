using System.Security.Cryptography;
using System.Text;

namespace WaterBilling.Infrastructure.Auth;

/// <summary>
/// Issues and verifies per-meter ingestion credentials (ADR-0005).
/// <para>
/// Keys are 256 bits of CSPRNG output, so a slow KDF is unnecessary — there is no
/// low-entropy secret to protect against offline guessing. SHA-256 with a
/// constant-time comparison is the appropriate primitive here, and it keeps the
/// per-request cost of ingestion negligible.
/// </para>
/// </summary>
public static class MeterApiKeyGenerator
{
    public const string HeaderName = "X-Meter-Key";

    private const string Prefix = "wmk_";

    public const int PrefixLength = 12;

    public static (string PlainTextKey, string Prefix, string Hash) Generate()
    {
        var entropy = RandomNumberGenerator.GetBytes(32);
        var key = Prefix + Base64Url(entropy);
        return (key, key[..PrefixLength], Hash(key));
    }

    public static string Hash(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(bytes);
    }

    public static bool Matches(string storedHash, string presentedKey) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(storedHash),
            Encoding.UTF8.GetBytes(Hash(presentedKey)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
