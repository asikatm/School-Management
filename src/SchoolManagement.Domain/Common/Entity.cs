namespace SchoolManagement.Domain.Common;

/// <summary>Surrogate-keyed base for every table (spec 5.4 design rules).</summary>
public abstract class Entity
{
    // Not mapped: set when the instance is loaded from, or saved to, the database.
    private bool _persisted;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// True for an instance created in code that has not been saved yet. Keys are generated client-side, so the
    /// persistence layer uses this (not the key value) to decide between INSERT and UPDATE.
    /// </summary>
    public bool IsTransient() => !_persisted;

    public void MarkPersisted() => _persisted = true;
}

/// <summary>Created/updated audit columns, stored in UTC.</summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAtUtc { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
    Guid? DeletedBy { get; set; }
}

/// <summary>Records owned by one campus (CORE-01). Users see only their campuses unless granted group-wide access.</summary>
public interface ICampusScoped
{
    Guid CampusId { get; set; }
}

/// <summary>Records that belong to an academic year; writes are blocked once the year is locked or archived (CORE-02, CORE-13).</summary>
public interface IAcademicYearScoped
{
    Guid AcademicYearId { get; set; }
}

/// <summary>Marks an entity whose changes must be written to the audit trail (CORE-05).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AuditedAttribute : Attribute;

public abstract class SoftDeleteEntity : AuditableEntity, ISoftDelete
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public Guid? DeletedBy { get; set; }
}

public abstract class CampusEntity : SoftDeleteEntity, ICampusScoped
{
    public Guid CampusId { get; set; }
}

/// <summary>
/// Campus-owned rows that are deleted for real, e.g. timetable cells and subject choices. They are rewritten often,
/// carry unique indexes that soft-deleted rows would block, and their history is kept by the audit trail instead.
/// </summary>
public abstract class CampusRecord : AuditableEntity, ICampusScoped
{
    public Guid CampusId { get; set; }
}
