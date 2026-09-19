using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaterBilling.Domain.Meters;
using WaterBilling.Infrastructure.Auth;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Infrastructure;

/// <summary>
/// Authenticates a meter by the <c>X-Meter-Key</c> header (ADR-0005).
/// <para>
/// This is a separate authentication scheme rather than a shortcut inside the
/// ingestion endpoint, so that "a device credential can never be used to read
/// invoices, and a customer JWT can never be used to post readings" is enforced by
/// the authorization policy rather than by remembering to check.
/// </para>
/// </summary>
public sealed class MeterKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    WaterBillingDbContext db,
    TimeProvider timeProvider) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "MeterKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(MeterApiKeyGenerator.HeaderName, out var values))
        {
            // No header at all: not a failure, just not this scheme's request.
            return AuthenticateResult.NoResult();
        }

        var presented = values.ToString();

        if (string.IsNullOrWhiteSpace(presented) || presented.Length < MeterApiKeyGenerator.PrefixLength)
        {
            return AuthenticateResult.Fail("Malformed meter key.");
        }

        var prefix = presented[..MeterApiKeyGenerator.PrefixLength];
        var now = timeProvider.GetUtcNow();

        // Narrow by the indexed prefix, then compare hashes in constant time. Without
        // the prefix this would hash every key in the estate on every reading.
        var candidates = await db.MeterApiKeys
            .AsNoTracking()
            .Include(k => k.Meter)
            .Where(k => k.Prefix == prefix)
            .ToListAsync(Context.RequestAborted);

        var match = candidates.FirstOrDefault(k =>
            k.IsUsable(now) && MeterApiKeyGenerator.Matches(k.KeyHash, presented));

        if (match?.Meter is not { } meter)
        {
            Logger.LogWarning("Rejected ingestion attempt with unknown or revoked meter key prefix {Prefix}.", prefix);
            return AuthenticateResult.Fail("Unknown, expired or revoked meter key.");
        }

        if (meter.Status is MeterStatus.Decommissioned)
        {
            return AuthenticateResult.Fail($"Meter {meter.SerialNumber} is decommissioned and no longer accepts readings.");
        }

        // Awaited, not fire-and-forget: the DbContext is request-scoped and would be
        // disposed underneath a detached task. At the design ingestion rate (300 meters
        // x 1 reading / 15 min = 0.33 writes/sec) one extra UPDATE is free.
        await TouchLastUsedAsync(match.Id, now);

        var identity = new ClaimsIdentity(
        [
            new Claim(MeterClaimTypes.MeterId, meter.Id.ToString()),
            new Claim(MeterClaimTypes.MeterSerial, meter.SerialNumber),
            new Claim(ClaimTypes.NameIdentifier, meter.Id.ToString())
        ], SchemeName);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    private async Task TouchLastUsedAsync(Guid keyId, DateTimeOffset now)
    {
        try
        {
            await db.MeterApiKeys
                .Where(k => k.Id == keyId)
                .ExecuteUpdateAsync(set => set.SetProperty(k => k.LastUsedAtUtc, now), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Could not update last-used timestamp for meter key {KeyId}.", keyId);
        }
    }
}
