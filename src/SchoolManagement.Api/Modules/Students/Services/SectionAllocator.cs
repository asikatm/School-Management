using SchoolManagement.Domain.Students;

namespace SchoolManagement.Api.Modules.Students.Services;

public enum AllocationStrategy
{
    /// <summary>Fill the emptiest section first, balancing gender when requested.</summary>
    Balanced = 0,

    /// <summary>Alphabetical blocks: A–x to the first section, and so on.</summary>
    Alphabetical = 1
}

public enum RollOrder
{
    Name = 0,
    AdmissionNo = 1,
    GenderThenName = 2
}

public record BulkAllocationRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, AllocationStrategy Strategy, bool BalanceGender,
    List<Guid>? StudentIds, bool OnlyUnallocated = true, bool DryRun = true);

public record AllocationLine(Guid StudentId, string AdmissionNo, string Name, string? Gender, Guid? FromSectionId, Guid ToSectionId, string ToSectionName);

public record BulkAllocationResult(bool Applied, IReadOnlyList<AllocationLine> Lines, IReadOnlyDictionary<string, int> SectionTotals);

public record RollGenerationRequest(Guid SectionId, RollOrder Order, int StartFrom = 1);

public interface ISectionAllocator
{
    Task<Guid?> PickSectionAsync(Guid campusId, Guid academicYearId, Guid yearGroupId, string? gender, CancellationToken ct);
    Task<int> NextRollAsync(Guid sectionId, CancellationToken ct);
    Task<BulkAllocationResult> BulkAllocateAsync(BulkAllocationRequest request, CancellationToken ct);
    Task<int> GenerateRollsAsync(RollGenerationRequest request, CancellationToken ct);
}

/// <summary>SIS-04: bulk section allocation, balancing by gender and roll-number generation rules.</summary>
public sealed class SectionAllocator(AppDbContext db, ICurrentUser currentUser) : ISectionAllocator
{
    public async Task<Guid?> PickSectionAsync(Guid campusId, Guid academicYearId, Guid yearGroupId, string? gender, CancellationToken ct)
    {
        var sections = await SectionLoadAsync(campusId, academicYearId, yearGroupId, ct);
        return sections
            .Where(s => s.Count < s.Capacity)
            .OrderBy(s => s.Count / (double)s.Capacity)
            .ThenBy(s => gender is null ? 0 : s.Genders.GetValueOrDefault(gender))
            .ThenBy(s => s.Name)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefault();
    }

    public async Task<int> NextRollAsync(Guid sectionId, CancellationToken ct)
    {
        var maxDb = await db.StudentEnrolments.Where(e => e.SectionId == sectionId && e.Status == EnrolmentStatus.Active)
            .MaxAsync(e => e.RollNo, ct) ?? 0;
        var maxLocal = db.StudentEnrolments.Local.Where(e => e.SectionId == sectionId).Select(e => e.RollNo ?? 0).DefaultIfEmpty(0).Max();
        return Math.Max(maxDb, maxLocal) + 1;
    }

    public async Task<BulkAllocationResult> BulkAllocateAsync(BulkAllocationRequest r, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Students.Edit, r.CampusId);
        var sections = await SectionLoadAsync(r.CampusId, r.AcademicYearId, r.YearGroupId, ct);
        if (sections.Count == 0) throw new DomainException("No sections exist for this class; create sections first.", "no_sections");

        var q = db.StudentEnrolments.Include(e => e.Student)
            .Where(e => e.CampusId == r.CampusId && e.AcademicYearId == r.AcademicYearId && e.YearGroupId == r.YearGroupId && e.Status == EnrolmentStatus.Active);
        if (r.StudentIds is { Count: > 0 }) q = q.Where(e => r.StudentIds.Contains(e.StudentId));
        else if (r.OnlyUnallocated) q = q.Where(e => e.SectionId == null);
        var enrolments = await q.ToListAsync(ct);

        // Students being moved no longer count towards their old section.
        foreach (var e in enrolments.Where(e => e.SectionId is not null))
        {
            var s = sections.First(x => x.Id == e.SectionId);
            s.Count--;
            if (e.Student!.Gender is { } g) s.Genders[g] = s.Genders.GetValueOrDefault(g) - 1;
        }

        var lines = new List<AllocationLine>();
        if (r.Strategy == AllocationStrategy.Alphabetical)
        {
            var ordered = enrolments.OrderBy(e => e.Student!.FirstName).ThenBy(e => e.Student!.LastName).ToList();
            var per = (int)Math.Ceiling(ordered.Count / (double)sections.Count);
            for (var i = 0; i < ordered.Count; i++)
                Assign(ordered[i], sections[Math.Min(i / Math.Max(per, 1), sections.Count - 1)]);
        }
        else
        {
            var ordered = r.BalanceGender
                ? enrolments.GroupBy(e => e.Student!.Gender ?? "").SelectMany(g => g.OrderBy(e => e.Student!.FirstName)).ToList()
                : enrolments.OrderBy(e => e.Student!.FirstName).ToList();
            foreach (var e in ordered)
            {
                var gender = e.Student!.Gender;
                var target = sections
                    .OrderBy(s => s.Count >= s.Capacity)
                    .ThenBy(s => r.BalanceGender && gender is not null ? s.Genders.GetValueOrDefault(gender) : 0)
                    .ThenBy(s => s.Count)
                    .ThenBy(s => s.Name)
                    .First();
                Assign(e, target);
            }
        }

        if (!r.DryRun)
        {
            foreach (var line in lines)
            {
                var e = enrolments.First(x => x.StudentId == line.StudentId);
                if (e.SectionId != line.ToSectionId)
                {
                    e.SectionId = line.ToSectionId;
                    e.RollNo = null;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        return new BulkAllocationResult(!r.DryRun, lines, sections.ToDictionary(s => s.Name, s => s.Count));

        void Assign(StudentEnrolment e, SectionLoad s)
        {
            s.Count++;
            if (e.Student!.Gender is { } g) s.Genders[g] = s.Genders.GetValueOrDefault(g) + 1;
            lines.Add(new AllocationLine(e.StudentId, e.Student.AdmissionNo, e.Student.FullName, e.Student.Gender, e.SectionId, s.Id, s.Name));
        }
    }

    public async Task<int> GenerateRollsAsync(RollGenerationRequest r, CancellationToken ct)
    {
        var section = await db.Sections.FirstOrDefaultAsync(s => s.Id == r.SectionId, ct) ?? throw new NotFoundException("Section", r.SectionId);
        currentUser.EnsurePermission(Permissions.Students.Edit, section.CampusId);

        var enrolments = await db.StudentEnrolments.Include(e => e.Student)
            .Where(e => e.SectionId == r.SectionId && e.Status == EnrolmentStatus.Active).ToListAsync(ct);
        IEnumerable<StudentEnrolment> ordered = r.Order switch
        {
            RollOrder.AdmissionNo => enrolments.OrderBy(e => e.Student!.AdmissionNo),
            RollOrder.GenderThenName => enrolments.OrderBy(e => e.Student!.Gender).ThenBy(e => e.Student!.FirstName).ThenBy(e => e.Student!.LastName),
            _ => enrolments.OrderBy(e => e.Student!.FirstName).ThenBy(e => e.Student!.LastName)
        };
        var roll = r.StartFrom;
        foreach (var e in ordered) e.RollNo = roll++;
        await db.SaveChangesAsync(ct);
        return enrolments.Count;
    }

    private sealed class SectionLoad
    {
        public Guid Id { get; init; }
        public required string Name { get; init; }
        public int Capacity { get; init; }
        public int Count { get; set; }
        public Dictionary<string, int> Genders { get; } = [];
    }

    private async Task<List<SectionLoad>> SectionLoadAsync(Guid campusId, Guid academicYearId, Guid yearGroupId, CancellationToken ct)
    {
        var sections = await db.Sections.AsNoTracking()
            .Where(s => s.CampusId == campusId && s.AcademicYearId == academicYearId && s.YearGroupId == yearGroupId)
            .OrderBy(s => s.Name).Select(s => new { s.Id, s.Name, s.Capacity }).ToListAsync(ct);
        var ids = sections.Select(s => s.Id).ToList();
        var counts = await db.StudentEnrolments.AsNoTracking()
            .Where(e => e.SectionId != null && ids.Contains(e.SectionId.Value) && e.Status == EnrolmentStatus.Active)
            .GroupBy(e => new { e.SectionId, e.Student!.Gender })
            .Select(g => new { g.Key.SectionId, g.Key.Gender, Count = g.Count() })
            .ToListAsync(ct);

        return sections.Select(s =>
        {
            var load = new SectionLoad { Id = s.Id, Name = s.Name, Capacity = s.Capacity };
            foreach (var c in counts.Where(c => c.SectionId == s.Id))
            {
                load.Count += c.Count;
                if (c.Gender is not null) load.Genders[c.Gender] = c.Count;
            }
            // Count students enrolled earlier in this same unit of work.
            load.Count += db.StudentEnrolments.Local.Count(e => e.SectionId == s.Id && db.Entry(e).State == EntityState.Added);
            return load;
        }).ToList();
    }
}
