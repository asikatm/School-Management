using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Students;

namespace SchoolManagement.Api.Modules.Students.Services;

public record RolloverRequest(string NewYearName, DateOnly StartDate, DateOnly EndDate, bool CopySections = true, bool CopySeatCapacities = true,
    bool PromoteStudents = true, bool LockSourceYear = false, bool MakeNewYearCurrent = false);

public record RolloverPreview(string SourceYear, int SectionsToCopy, int ActiveStudents, int ToPromote, int ToGraduate, IReadOnlyList<string> Warnings);

public record RolloverResult(Guid NewAcademicYearId, string NewYearName, int TermsCreated, int SectionsCreated, int SeatCapacitiesCopied,
    PromotionApplyResult? Promotion, bool SourceLocked, IReadOnlyList<string> Notes);

public interface IRolloverService
{
    Task<RolloverPreview> PreviewAsync(Guid sourceYearId, CancellationToken ct);
    Task<RolloverResult> RunAsync(Guid sourceYearId, RolloverRequest request, CancellationToken ct);
}

/// <summary>CORE-02 roll-over wizard: new year, class re-creation, promotion and (optionally) locking the old year.</summary>
public sealed class RolloverService(AppDbContext db, IPromotionService promotion) : IRolloverService
{
    public async Task<RolloverPreview> PreviewAsync(Guid sourceYearId, CancellationToken ct)
    {
        var year = await db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == sourceYearId, ct) ?? throw new NotFoundException("Academic year", sourceYearId);
        var finalGroups = await db.YearGroups.Where(y => y.IsFinalYear || y.NextYearGroupId == null).Select(y => y.Id).ToListAsync(ct);
        var active = db.StudentEnrolments.Where(e => e.AcademicYearId == sourceYearId && e.Status == EnrolmentStatus.Active);

        var warnings = new List<string>();
        var pendingApps = await db.Applications.CountAsync(a => a.AcademicYearId == sourceYearId &&
            (a.Status == ApplicationStatus.Offered || a.Status == ApplicationStatus.Accepted), ct);
        if (pendingApps > 0) warnings.Add($"{pendingApps} applications for this year are still offered/accepted but not enrolled.");
        var unsectioned = await active.CountAsync(e => e.SectionId == null, ct);
        if (unsectioned > 0) warnings.Add($"{unsectioned} active students have no section.");

        return new RolloverPreview(year.Name,
            await db.Sections.CountAsync(s => s.AcademicYearId == sourceYearId, ct),
            await active.CountAsync(ct),
            await active.CountAsync(e => !finalGroups.Contains(e.YearGroupId), ct),
            await active.CountAsync(e => finalGroups.Contains(e.YearGroupId), ct),
            warnings);
    }

    public async Task<RolloverResult> RunAsync(Guid sourceYearId, RolloverRequest request, CancellationToken ct)
    {
        var source = await db.AcademicYears.Include(y => y.Terms).FirstOrDefaultAsync(y => y.Id == sourceYearId, ct)
                     ?? throw new NotFoundException("Academic year", sourceYearId);
        if (source.Status is not (AcademicYearStatus.Open or AcademicYearStatus.Draft))
            throw new DomainException("Roll-over must start from an open year (lock it at the end of the wizard).", "year_locked", isConflict: true);
        if (request.StartDate <= source.StartDate)
            throw new DomainException("The new year must start after the source year.", "invalid_dates");
        if (await db.AcademicYears.AnyAsync(y => y.Name == request.NewYearName, ct))
            throw new DomainException($"Academic year '{request.NewYearName}' already exists.", "duplicate", isConflict: true);

        // A user transaction must run inside the provider's execution strategy when retry-on-failure is enabled.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await RunCoreAsync(source, request, ct);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    private async Task<RolloverResult> RunCoreAsync(AcademicYear source, RolloverRequest request, CancellationToken ct)
    {
        var sourceYearId = source.Id;
        var notes = new List<string>();
        var shift = request.StartDate.DayNumber - source.StartDate.DayNumber;
        var target = new AcademicYear
        {
            OrganisationId = source.OrganisationId, Name = request.NewYearName, StartDate = request.StartDate, EndDate = request.EndDate,
            Status = AcademicYearStatus.Open, RolledOverFromId = source.Id
        };
        foreach (var t in source.Terms.OrderBy(t => t.Sequence))
        {
            var start = t.StartDate.AddDays(shift);
            var end = t.EndDate.AddDays(shift);
            if (end > request.EndDate) end = request.EndDate;
            target.Terms.Add(new Term { Name = t.Name, NameBn = t.NameBn, Sequence = t.Sequence, StartDate = start, EndDate = end });
        }
        db.AcademicYears.Add(target);

        var sectionsCreated = 0;
        if (request.CopySections)
        {
            var sections = await db.Sections.AsNoTracking().Where(s => s.AcademicYearId == sourceYearId).ToListAsync(ct);
            foreach (var s in sections)
            {
                db.Sections.Add(new Section
                {
                    CampusId = s.CampusId, AcademicYearId = target.Id, YearGroupId = s.YearGroupId, ShiftId = s.ShiftId,
                    Name = s.Name, Capacity = s.Capacity, Room = s.Room
                });
                sectionsCreated++;
            }
        }

        var seatsCopied = 0;
        if (request.CopySeatCapacities)
        {
            var seats = await db.SeatCapacities.AsNoTracking().Where(s => s.AcademicYearId == sourceYearId).ToListAsync(ct);
            foreach (var s in seats)
            {
                db.SeatCapacities.Add(new SeatCapacity { CampusId = s.CampusId, AcademicYearId = target.Id, YearGroupId = s.YearGroupId, TotalSeats = s.TotalSeats });
                seatsCopied++;
            }
        }
        await db.SaveChangesAsync(ct);

        PromotionApplyResult? promotionResult = null;
        if (request.PromoteStudents)
        {
            var campuses = await db.Campuses.Select(c => c.Id).ToListAsync(ct);
            var decisions = new List<PromotionDecision>();
            foreach (var campusId in campuses)
            {
                var candidates = await promotion.PreviewAsync(sourceYearId, campusId, null, ct);
                decisions.AddRange(candidates.Select(c => new PromotionDecision(c.StudentId, c.SuggestedOutcome, null, "Year-end roll-over")));
            }
            promotionResult = await promotion.ApplyAsync(new PromotionApplyRequest(sourceYearId, target.Id, decisions), ct);
        }

        if (request.MakeNewYearCurrent)
        {
            source.IsCurrent = false;
            target.IsCurrent = true;
        }
        if (request.LockSourceYear)
        {
            if (source.IsCurrent) throw new DomainException("Make the new year current to lock the source year in the same run.", "year_is_current");
            source.Status = AcademicYearStatus.Locked;
        }
        await db.SaveChangesAsync(ct);

        notes.Add("Fee carry-forward runs when the FEE module is installed (release 2).");
        return new RolloverResult(target.Id, target.Name, target.Terms.Count, sectionsCreated, seatsCopied, promotionResult, request.LockSourceYear, notes);
    }
}
