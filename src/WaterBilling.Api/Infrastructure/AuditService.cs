using System.Security.Claims;
using System.Text.Json;
using WaterBilling.Domain.Common;
using WaterBilling.Infrastructure.Persistence;

namespace WaterBilling.Api.Infrastructure;

/// <summary>
/// Records state-changing administrative actions. Billing gets disputed, and
/// "who changed this tariff, and when" needs an answer that does not depend on
/// application logs having been retained or shipped anywhere.
/// <para>
/// Entries are added to the current unit of work but NOT saved here: the caller
/// saves once, so the audit entry and the change it describes commit together or
/// not at all. An audit log that can disagree with the data is worse than none.
/// </para>
/// </summary>
public sealed class AuditService(
    WaterBillingDbContext db,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider)
{
    public void Record(string action, string entityType, Guid? entityId, object? changes = null)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var principal = httpContext?.User;

        db.AuditLog.Add(new AuditLogEntry
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAtUtc = timeProvider.GetUtcNow(),
            ActorUserId = TryGetUserId(principal),
            ActorEmail = principal?.FindFirstValue(ClaimTypes.Email) ?? principal?.Identity?.Name,
            IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
            ChangesJson = changes is null ? null : JsonSerializer.Serialize(changes, SerializerOptions)
        });
    }

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static Guid? TryGetUserId(ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
