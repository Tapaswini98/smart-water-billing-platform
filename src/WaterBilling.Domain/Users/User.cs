using WaterBilling.Domain.Common;
using WaterBilling.Domain.Meters;

namespace WaterBilling.Domain.Users;

public sealed class User : Entity
{
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public required string FullName { get; set; }

    public UserRole Role { get; set; } = UserRole.Customer;

    public string? PhoneNumber { get; set; }

    public string? BillingAddress { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    /// <summary>Meters assigned to this user. Empty for admins.</summary>
    public ICollection<Meter> Meters { get; set; } = [];
}
