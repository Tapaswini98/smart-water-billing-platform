namespace WaterBilling.Domain.Common;

/// <summary>
/// Base for persisted aggregates. Soft delete is modelled as a nullable
/// <see cref="DeletedAtUtc"/> timestamp rather than a boolean flag: it answers
/// "was this deleted?" and "when?" with one column, and a global EF query
/// filter keeps deleted rows out of every read path automatically.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }

    public bool IsDeleted => DeletedAtUtc is not null;
}
