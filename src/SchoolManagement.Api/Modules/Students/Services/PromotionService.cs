using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Students;

namespace SchoolManagement.Api.Modules.Students.Services;

public enum PromotionOutcome
{
    Promote = 0,
    Conditional = 1,
    Repeat = 2,
    Graduate = 3,
    Hold = 4
}

public record PromotionCandidate(Guid StudentId, string AdmissionNo, string Name, Guid YearGroupId, string YearGroupName, Guid? SectionId, int? RollNo,
    PromotionOutcome SuggestedOutcome, Guid? TargetYearGroupId, string? TargetYearGroupName, IReadOnlyList<string> Notes);

public record PromotionDecision(Guid StudentId, PromotionOutcome Outcome, Guid? TargetSectionId, string? Note);

public record PromotionApplyRequest(Guid SourceAcademicYearId, Guid TargetAcademicYearId, List<PromotionDecision> Decisions);

public record PromotionApplyResult(int Promoted, int Conditional, int Repeated, int Graduated, int Held);

/// <summary>
/// Hook for modules that inform the year-end decision (EXM results, ATT attendance thresholds). Returns a
/// suggested outcome and notes; the review screen shows them and staff confirm.
/// </summary>
public interface IPromotionRule
{
    Task<(PromotionOutcome? Outcome, string? Note)> EvaluateAsync(StudentEnrolment enrolment, CancellationToken ct);
}

public interface IPromotionService
{
    Task<List<PromotionCandidate>> PreviewAsync(Guid sourceYearId, Guid campusId, Guid? yearGroupId, CancellationToken ct);
    Task<PromotionApplyResult> ApplyAsync(PromotionApplyRequest request, CancellationToken ct);
}

/// <summary>SIS-05: rule-based year-end promotion with a review screen, repeat/hold, IGCSE → IAL transition and graduation.</summary>
public sealed class PromotionService(AppDbContext db, ICurrentUser currentUser, IEnumerable<IPromotionRule> rules) : IPromotionService
{
    public async Task<List<PromotionCandidate>> PreviewAsync(Guid sourceYearId, Guid campusId, Guid? yearGroupId, CancellationToken ct)
    {
        var yearGroups = await db.YearGroups.AsNoTracking().ToDictionaryAsync(y => y.Id, ct);
        var enrolments = await db.StudentEnrolments.Include(e => e.Student)
            .Where(e => e.AcademicYearId == sourceYearId && e.CampusId == campusId && e.Status == EnrolmentStatus.Active &&
                        (yearGroupId == null || e.YearGroupId == yearGroupId))
            .OrderBy(e => e.YearGroupId).ThenBy(e => e.SectionId).ThenBy(e => e.RollNo)
            .ToListAsync(ct);

        var result = new List<PromotionCandidate>();
        foreach (var e in enrolments)
        {
            var yg = yearGroups[e.YearGroupId];
            var notes = new List<string>();
            var outcome = yg.IsFinalYear || yg.NextYearGroupId is null ? PromotionOutcome.Graduate : PromotionOutcome.Promote;
            foreach (var rule in rules)
            {
                var (o, note) = await rule.EvaluateAsync(e, ct);
                if (note is not null) notes.Add(note);
                if (o is { } ruled && ruled > outcome && outcome != PromotionOutcome.Graduate) outcome = ruled;
            }
            var target = outcome == PromotionOutcome.Repeat ? yg : yg.NextYearGroupId is { } next ? yearGroups[next] : null;
            result.Add(new PromotionCandidate(e.StudentId, e.Student!.AdmissionNo, e.Student.FullName, yg.Id, yg.Name, e.SectionId, e.RollNo,
                outcome, outcome == PromotionOutcome.Graduate ? null : target?.Id, outcome == PromotionOutcome.Graduate ? null : target?.Name, notes));
        }
        return result;
    }

    public async Task<PromotionApplyResult> ApplyAsync(PromotionApplyRequest request, CancellationToken ct)
    {
        if (request.SourceAcademicYearId == request.TargetAcademicYearId)
            throw new DomainException("Source and target years must differ.", "same_year");

        var target = await db.AcademicYears.FirstOrDefaultAsync(y => y.Id == request.TargetAcademicYearId, ct) ?? throw new NotFoundException("Academic year", request.TargetAcademicYearId);
        if (!target.IsWritable) throw new DomainException("The target year is locked.", "year_locked", isConflict: true);

        var yearGroups = await db.YearGroups.AsNoTracking().ToDictionaryAsync(y => y.Id, ct);
        var studentIds = request.Decisions.Select(d => d.StudentId).ToList();
        var enrolments = await db.StudentEnrolments.Include(e => e.Student)
            .Where(e => e.AcademicYearId == request.SourceAcademicYearId && e.Status == EnrolmentStatus.Active && studentIds.Contains(e.StudentId))
            .ToListAsync(ct);

        var targetSections = await db.Sections.AsNoTracking().Where(s => s.AcademicYearId == request.TargetAcademicYearId)
            .Select(s => new { s.Id, s.CampusId, s.YearGroupId, s.Name }).ToListAsync(ct);
        var sourceSectionNames = await db.Sections.AsNoTracking().Where(s => s.AcademicYearId == request.SourceAcademicYearId)
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        int promoted = 0, conditional = 0, repeated = 0, graduated = 0, held = 0;
        foreach (var decision in request.Decisions)
        {
            var e = enrolments.FirstOrDefault(x => x.StudentId == decision.StudentId)
                    ?? throw new DomainException($"Student {decision.StudentId} has no active enrolment in the source year.", "no_enrolment");
            currentUser.EnsurePermission(Permissions.Students.Approve, e.CampusId);
            var yg = yearGroups[e.YearGroupId];

            if (decision.Outcome == PromotionOutcome.Hold) { held++; continue; }

            e.EndDate = target.StartDate.AddDays(-1);
            e.Note = decision.Note;

            if (decision.Outcome == PromotionOutcome.Graduate)
            {
                e.Status = EnrolmentStatus.Graduated;
                e.Student!.Status = StudentStatus.Graduated;
                e.Student.LeftOn = e.EndDate;
                e.Student.CurrentEnrolmentId = null;
                graduated++;
                continue;
            }

            Guid nextYearGroup;
            if (decision.Outcome == PromotionOutcome.Repeat)
            {
                e.Status = EnrolmentStatus.Repeated;
                nextYearGroup = yg.Id;
                repeated++;
            }
            else
            {
                nextYearGroup = yg.NextYearGroupId ?? throw new DomainException($"{yg.Name} has no next class; graduate the student instead.", "no_next_class");
                e.Status = EnrolmentStatus.Promoted;
                if (decision.Outcome == PromotionOutcome.Conditional) conditional++; else promoted++;
            }

            // Keep the same section letter where the target year has one, unless a section was chosen on the review screen.
            var sectionName = e.SectionId is { } sid ? sourceSectionNames.GetValueOrDefault(sid) : null;
            var sectionId = decision.TargetSectionId
                            ?? targetSections.FirstOrDefault(s => s.CampusId == e.CampusId && s.YearGroupId == nextYearGroup && s.Name == sectionName)?.Id;

            var next = new StudentEnrolment
            {
                StudentId = e.StudentId, CampusId = e.CampusId, AcademicYearId = target.Id, YearGroupId = nextYearGroup,
                SectionId = sectionId, StartDate = target.StartDate,
                Note = decision.Outcome == PromotionOutcome.Conditional ? $"Conditional promotion. {decision.Note}".Trim() : null
            };
            db.StudentEnrolments.Add(next);
            e.Student!.CurrentEnrolmentId = next.Id;
        }

        await db.SaveChangesAsync(ct);
        return new PromotionApplyResult(promoted, conditional, repeated, graduated, held);
    }
}
