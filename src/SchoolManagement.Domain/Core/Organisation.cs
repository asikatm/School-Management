using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Core;

public class Organisation : SoftDeleteEntity
{
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public string? Code { get; set; }
    public string? Website { get; set; }
    public string TimeZone { get; set; } = "Asia/Dhaka";
    public ICollection<Campus> Campuses { get; set; } = [];
}

/// <summary>CORE-01: one installation, many campuses.</summary>
[Audited]
public class Campus : SoftDeleteEntity
{
    public Guid OrganisationId { get; set; }
    public Organisation? Organisation { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? HeadName { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum AcademicYearStatus
{
    Draft = 0,
    Open = 1,
    Locked = 2,
    Archived = 3
}

/// <summary>CORE-02: academic years with open / lock / roll-over / archive lifecycle.</summary>
[Audited]
public class AcademicYear : SoftDeleteEntity
{
    public Guid OrganisationId { get; set; }
    public required string Name { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public AcademicYearStatus Status { get; set; } = AcademicYearStatus.Draft;
    public bool IsCurrent { get; set; }
    public Guid? RolledOverFromId { get; set; }
    public ICollection<Term> Terms { get; set; } = [];

    public bool IsWritable => Status is AcademicYearStatus.Draft or AcademicYearStatus.Open;
}

public class Term : SoftDeleteEntity
{
    public Guid AcademicYearId { get; set; }
    public AcademicYear? AcademicYear { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public int Sequence { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool IsLocked { get; set; }
}
