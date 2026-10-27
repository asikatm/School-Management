using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Core;

namespace SchoolManagement.Api.Modules.Learning;

public record RosterStudent(Guid StudentId, string AdmissionNo, string Name, int? RollNo);

/// <summary>Who is in a class, and who of them takes a subject (compulsory subjects: everyone; options and electives: their choosers).</summary>
public sealed class ClassRoster(AppDbContext db)
{
    public async Task<List<RosterStudent>> OfSectionAsync(Guid sectionId, DateOnly date, CancellationToken ct)
    {
        var rows = await db.StudentEnrolments.IgnoreQueryFilters().AsNoTracking()
            .Where(e => !e.IsDeleted && e.SectionId == sectionId && e.StartDate <= date && (e.EndDate == null || e.EndDate >= date))
            .Select(e => new RosterStudent(e.StudentId, e.Student!.AdmissionNo, e.Student.FirstName + (e.Student.LastName == null ? "" : " " + e.Student.LastName), e.RollNo))
            .ToListAsync(ct);
        return rows.DistinctBy(r => r.StudentId).OrderBy(r => r.RollNo ?? int.MaxValue).ThenBy(r => r.Name).ToList();
    }

    public async Task<List<RosterStudent>> TakingAsync(SectionInfo section, Guid subjectId, DateOnly date, CancellationToken ct)
    {
        var all = await OfSectionAsync(section.Id, date, ct);
        var ids = all.Select(s => s.StudentId).ToList();
        var takers = await TakersAsync(section.YearGroupId, section.AcademicYearId, subjectId, ids, ct);
        return all.Where(s => takers.Contains(s.StudentId)).ToList();
    }

    /// <summary>The students among <paramref name="studentIds"/> who take the subject in that class and year.</summary>
    public async Task<HashSet<Guid>> TakersAsync(Guid yearGroupId, Guid academicYearId, Guid subjectId, IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        var type = await db.CurriculumSubjects.AsNoTracking().Where(c => c.YearGroupId == yearGroupId && c.SubjectId == subjectId)
            .Select(c => (SubjectType?)c.Type).FirstOrDefaultAsync(ct);
        if (type is null or SubjectType.Compulsory) return studentIds.ToHashSet();
        return (await db.StudentSubjects.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.AcademicYearId == academicYearId && s.SubjectId == subjectId && studentIds.Contains(s.StudentId))
            .Select(s => s.StudentId).ToListAsync(ct)).ToHashSet();
    }
}

/// <summary>
/// "Own classes" rule for teaching work: the class teacher of a section, or a teacher allocated to the subject there.
/// With <c>subjectOnly</c> (exam marks: spec 5.6 "own subjects") only the subject allocation counts.
/// Holders of the override permission on the campus may act on any class.
/// </summary>
public sealed class TeachingAccess(AppDbContext db, ICurrentUser currentUser)
{
    public async Task<bool> TeachesAsync(SectionInfo section, Guid? subjectId, CancellationToken ct, bool subjectOnly = false)
    {
        if (currentUser.UserId is not { } me) return false;
        if (section.ClassTeacherUserId == me && !(subjectOnly && subjectId is not null)) return true;
        return await db.TeacherAllocations.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(a => a.SectionId == section.Id && a.TeacherUserId == me && (subjectId == null || a.SubjectId == subjectId), ct);
    }

    public async Task<bool> CanAsync(SectionInfo section, Guid? subjectId, string permission, string overridePermission, CancellationToken ct, bool subjectOnly = false)
    {
        if (currentUser.HasPermission(overridePermission, section.CampusId)) return true;
        return currentUser.HasPermission(permission, section.CampusId) && await TeachesAsync(section, subjectId, ct, subjectOnly);
    }

    public async Task EnsureAsync(SectionInfo section, Guid? subjectId, string permission, string overridePermission, CancellationToken ct, bool subjectOnly = false)
    {
        if (!await CanAsync(section, subjectId, permission, overridePermission, ct, subjectOnly))
            throw new ForbiddenException(subjectOnly
                ? "Only a teacher of this subject in the class can do this."
                : "Only the class teacher or a teacher of this subject in the class can do this.");
    }

    public void EnsureCampus(Guid campusId)
    {
        if (!currentUser.CanAccessCampus(campusId)) throw new ForbiddenException("You do not have access to this campus.");
    }
}

/// <summary>Portal access: a guardian sees their children, a student sees themself (PRT).</summary>
public sealed class PortalAccess(AppDbContext db, ICurrentUser currentUser)
{
    public async Task<List<Guid>> MyStudentsAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } me) return [];
        var ids = new List<Guid>();
        if (currentUser.HasPermission(Permissions.Portal.Parent))
            ids.AddRange(await db.StudentGuardians.IgnoreQueryFilters().AsNoTracking()
                .Where(g => g.Guardian!.UserId == me && !g.Guardian.IsDeleted).Select(g => g.StudentId).ToListAsync(ct));
        if (currentUser.HasPermission(Permissions.Portal.Student))
            ids.AddRange(await db.Students.IgnoreQueryFilters().AsNoTracking().Where(s => s.UserId == me && !s.IsDeleted).Select(s => s.Id).ToListAsync(ct));
        return ids.Distinct().ToList();
    }

    public async Task EnsureAsync(Guid studentId, CancellationToken ct)
    {
        if (!currentUser.HasPermission(Permissions.Portal.Parent) && !currentUser.HasPermission(Permissions.Portal.Student))
            throw new ForbiddenException("This page is for parents and students.");
        if (!(await MyStudentsAsync(ct)).Contains(studentId)) throw new ForbiddenException("This student is not linked to your account.");
    }
}

public static class LearningText
{
    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
