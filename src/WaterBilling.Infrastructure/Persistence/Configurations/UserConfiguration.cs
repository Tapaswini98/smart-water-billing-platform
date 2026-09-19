using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WaterBilling.Domain.Users;

namespace WaterBilling.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.PhoneNumber).HasMaxLength(32);
        builder.Property(u => u.BillingAddress).HasMaxLength(1024);
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32).IsRequired();

        // Email is the login identifier, so uniqueness must be case-insensitive and
        // must not be defeated by soft-deleting a user: a filtered unique index lets
        // a deleted address be reused, while live addresses stay unique.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName("ix_users_email_unique")
            .HasFilter("deleted_at_utc IS NULL");

        builder.HasIndex(u => u.Role).HasDatabaseName("ix_users_role");

        builder.HasQueryFilter(u => u.DeletedAtUtc == null);
    }
}
