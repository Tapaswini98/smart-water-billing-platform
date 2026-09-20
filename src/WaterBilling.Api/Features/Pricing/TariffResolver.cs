using Microsoft.EntityFrameworkCore;
using WaterBilling.Domain.Pricing;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Features.Pricing;

/// <summary>
/// Finds the tariff version that applies to a billing period and converts it into
/// the plain snapshot the pricing engine consumes.
/// <para>
/// Per ADR-0004 the version effective at <em>period end</em> prices the whole period.
/// One rule, applied in one place, so billing and the admin's price preview cannot
/// disagree about which rates a customer is on.
/// </para>
/// </summary>
public sealed class TariffResolver(WaterBillingDbContext db)
{
    public async Task<TariffSnapshot?> ResolveAsync(
        Guid pricingPlanId,
        DateTimeOffset effectiveAt,
        CancellationToken cancellationToken)
    {
        var plan = await db.PricingPlans
            .AsNoTracking()
            .Where(p => p.Id == pricingPlanId)
            .Select(p => new { p.Id, p.Name, p.Currency })
            .FirstOrDefaultAsync(cancellationToken);

        if (plan is null)
        {
            return null;
        }

        var version = await db.PricingPlanVersions
            .AsNoTracking()
            .Include(v => v.Slabs)
            .Where(v => v.PricingPlanId == pricingPlanId && v.EffectiveFromUtc <= effectiveAt)
            .Where(v => v.EffectiveToUtc == null || v.EffectiveToUtc > effectiveAt)
            .OrderByDescending(v => v.EffectiveFromUtc)
            .FirstOrDefaultAsync(cancellationToken);

        // Null rather than a fallback to "the newest version": billing a period at
        // rates that were not in force during it would be wrong, and silently so.
        return version is null ? null : TariffSnapshot.From(version, plan.Name, plan.Currency);
    }

    /// <summary>The version currently in force, for previews and the admin UI.</summary>
    public Task<TariffSnapshot?> ResolveCurrentAsync(Guid pricingPlanId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ResolveAsync(pricingPlanId, now, cancellationToken);
}
