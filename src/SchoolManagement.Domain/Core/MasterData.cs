using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Core;

/// <summary>Stage such as Early Years, Primary, Lower Secondary, International GCSE, International A Level.</summary>
public class Programme : SoftDeleteEntity
{
    public Guid OrganisationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public int Sequence { get; set; }
    public Guid? DefaultGradingSchemeId { get; set; }
    public GradingScheme? DefaultGradingScheme { get; set; }
    public ICollection<YearGroup> YearGroups { get; set; } = [];
}

/// <summary>A class / year-group (e.g. Year 7). <see cref="NextYearGroupId"/> drives promotion (SIS-05).</summary>
public class YearGroup : SoftDeleteEntity
{
    public Guid ProgrammeId { get; set; }
    public Programme? Programme { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public int Sequence { get; set; }
    public Guid? NextYearGroupId { get; set; }
    public bool IsFinalYear { get; set; }

    /// <summary>Admission age cut-off in months on <see cref="AgeCutOffMonth"/>/<see cref="AgeCutOffDay"/> (ADM-05).</summary>
    public int? MinAgeMonths { get; set; }
    public int? MaxAgeMonths { get; set; }
    public int AgeCutOffMonth { get; set; } = 1;
    public int AgeCutOffDay { get; set; } = 1;
}

public class Shift : CampusEntity
{
    public required string Name { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}

/// <summary>A section of a year-group on one campus for one academic year (e.g. Year 7 - A, Dhanmondi, 2026-27).</summary>
public class Section : CampusEntity, IAcademicYearScoped
{
    public Guid AcademicYearId { get; set; }
    public Guid YearGroupId { get; set; }
    public YearGroup? YearGroup { get; set; }
    public Guid? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public required string Name { get; set; }
    public int Capacity { get; set; } = 30;
    public Guid? ClassTeacherUserId { get; set; }
    public string? Room { get; set; }
}

public enum SubjectType
{
    Compulsory = 0,
    Optional = 1,
    Elective = 2
}

public class Subject : SoftDeleteEntity
{
    public Guid OrganisationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public SubjectType Type { get; set; }
    public string? ExternalCode { get; set; }
}

public class House : SoftDeleteEntity
{
    public Guid OrganisationId { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public string? Colour { get; set; }
}

public enum HolidayType
{
    National = 0,
    Islamic = 1,
    School = 2
}

public class Holiday : SoftDeleteEntity
{
    public Guid AcademicYearId { get; set; }

    /// <summary>Null means the holiday applies to every campus.</summary>
    public Guid? CampusId { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public HolidayType Type { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public enum GradingScaleType
{
    Descriptor = 0,
    Letter = 1,
    Numeric9To1 = 2,
    LetterAStarToE = 3,
    Gpa5 = 4
}

/// <summary>Configurable percentage → grade mapping (CORE-07, Appendix A).</summary>
public class GradingScheme : SoftDeleteEntity
{
    public Guid OrganisationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public GradingScaleType ScaleType { get; set; }
    public ICollection<GradeBand> Bands { get; set; } = [];
}

public class GradeBand : Entity
{
    public Guid GradingSchemeId { get; set; }
    public required string Grade { get; set; }
    public string? Descriptor { get; set; }
    public string? DescriptorBn { get; set; }
    public decimal MinPercent { get; set; }
    public decimal MaxPercent { get; set; }
    public decimal? GradePoint { get; set; }
    public bool IsPass { get; set; } = true;
}

public enum FeeFrequency
{
    OneTime = 0,
    Monthly = 1,
    Termly = 2,
    Annual = 3
}

/// <summary>Fee head master (CORE-07); amounts and plans are configured in the FEE module.</summary>
public class FeeHead : SoftDeleteEntity
{
    public Guid OrganisationId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? NameBn { get; set; }
    public FeeFrequency Frequency { get; set; }
    public bool IsRefundable { get; set; }
}

/// <summary>
/// Numbering formats for admission no., roll no., invoice no., etc. (CORE-07).
/// Pattern tokens: {PREFIX} {CAMPUS} {YEAR} {YY} {SEQ:n}.
/// </summary>
public class NumberSequence : Entity
{
    public required string Key { get; set; }
    public Guid? CampusId { get; set; }
    public Guid? AcademicYearId { get; set; }
    public string Prefix { get; set; } = "";
    public string Pattern { get; set; } = "{PREFIX}{YY}{SEQ:5}";
    public long NextValue { get; set; } = 1;

    /// <summary>Optimistic concurrency token so two cashiers never get the same number.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();
}

/// <summary>Key/value settings such as SMS sender ID, letterheads, signature images (CORE-07).</summary>
public class SystemSetting : AuditableEntity
{
    public required string Key { get; set; }
    public Guid? CampusId { get; set; }
    public string? Value { get; set; }
    public string? Description { get; set; }
}
