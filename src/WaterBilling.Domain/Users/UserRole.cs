namespace WaterBilling.Domain.Users;

public enum UserRole
{
    /// <summary>Full access: manages users, meters, pricing plans and billing runs.</summary>
    Admin = 1,

    /// <summary>Can read only the meters assigned to them and their own invoices.</summary>
    Customer = 2
}
