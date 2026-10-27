using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Attendance;
using SchoolManagement.Api.Modules.Learning;
using SchoolManagement.Domain.Communication;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Notifications;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Communication;

public sealed class NoticeForm
{
    public Guid? CampusId { get; init; }
    public Guid? YearGroupId { get; init; }
    public required string Title { get; init; }
    public string? TitleBn { get; init; }
    public required string Body { get; init; }
    public bool ForStudents { get; init; } = true;
    public bool ForParents { get; init; } = true;
    public bool ForStaff { get; init; }
    public bool IsPinned { get; init; }
    public bool IsUrgent { get; init; }
    public DateOnly? ExpiresOn { get; init; }
    public IFormFile? Attachment { get; init; }
}

public record NoticeView(Guid Id, Guid? CampusId, string? CampusName, Guid? YearGroupId, string? ClassName, string Title, string? TitleBn, string Body,
    bool ForStudents, bool ForParents, bool ForStaff, bool IsPinned, bool IsUrgent, DateTime PublishedAtUtc, DateOnly? ExpiresOn, bool HasAttachment,
    string? AttachmentName, string? PostedBy, bool Active, bool CanEdit);

/// <summary>
/// Notice board: notices and circulars for students, parents and/or staff, by campus and class. Publishing tells the
/// audience in-app; urgent notices also go to the guardians' phones by SMS (Appendix C).
/// </summary>
public sealed class NoticeService(
    AppDbContext db,
    AcademicLookups lookups,
    SchoolDays schoolDays,
    PortalAccess portal,
    INotificationService notifications,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    /// <summary>Staff view: managers see every notice they may edit (also expired); others see the staff board.</summary>
    public async Task<List<NoticeView>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        if (!await IsStaffAsync(ct)) throw new ForbiddenException("The staff notice board is for staff; parents and students see notices in the portal.");
        var rows = await db.Notices.AsNoTracking().OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.PublishedAtUtc).Take(500).ToListAsync(ct);
        var manager = currentUser.HasPermission(Permissions.Notices.Manage);
        rows = rows.Where(n => n.CampusId is null || currentUser.CanAccessCampus(n.CampusId.Value))
            .Where(n => (manager && includeInactive) || (n.ForStaff && IsActive(n)) || (manager && IsActive(n))).ToList();
        return await ViewsAsync(rows, ct);
    }

    public async Task<NoticeView> CreateAsync(NoticeForm form, CancellationToken ct)
    {
        EnsureManage(form.CampusId);
        Validate(form);
        var n = new Notice { Title = form.Title.Trim(), Body = form.Body.Trim(), PublishedAtUtc = clock.GetUtcNow().UtcDateTime };
        Apply(n, form);
        await SaveAttachmentAsync(n, form.Attachment, ct);
        db.Notices.Add(n);
        await AnnounceAsync(n, ct);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([n], ct))[0];
    }

    public async Task<NoticeView> UpdateAsync(Guid id, NoticeForm form, CancellationToken ct)
    {
        var n = await db.Notices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Notice", id);
        EnsureManage(n.CampusId);
        EnsureManage(form.CampusId);
        Validate(form);
        n.Title = form.Title.Trim();
        n.Body = form.Body.Trim();
        Apply(n, form);
        if (form.Attachment is { Length: > 0 })
        {
            if (n.AttachmentPath is not null) storage.Delete(n.AttachmentPath);
            await SaveAttachmentAsync(n, form.Attachment, ct);
        }
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([n], ct))[0];
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var n = await db.Notices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Notice", id);
        EnsureManage(n.CampusId);
        db.Notices.Remove(n);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The board a student (student notices) or guardian (parent notices) sees for that child.</summary>
    public async Task<List<NoticeView>> ForStudentAsync(Guid studentId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        var (campusId, yearGroupId, self) = await TargetOfAsync(studentId, ct);
        var rows = await db.Notices.IgnoreQueryFilters().AsNoTracking()
            .Where(n => !n.IsDeleted && (n.CampusId == null || n.CampusId == campusId) && (n.YearGroupId == null || n.YearGroupId == yearGroupId)
                        && (self ? n.ForStudents : n.ForParents))
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.PublishedAtUtc).Take(200).ToListAsync(ct);
        return await ViewsAsync(rows.Where(IsActive).ToList(), ct);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> AttachmentAsync(Guid id, Guid? studentId, CancellationToken ct)
    {
        var n = await db.Notices.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct) ?? throw new NotFoundException("Notice", id);
        if (studentId is { } sid)
        {
            if ((await ForStudentAsync(sid, ct)).All(x => x.Id != id)) throw new ForbiddenException("This notice is not for you.");
        }
        else if (!await IsStaffAsync(ct) || !(n.ForStaff || currentUser.HasPermission(Permissions.Notices.Manage)) || (n.CampusId is { } c && !currentUser.CanAccessCampus(c)))
            throw new ForbiddenException("This notice is not for you.");
        if (n.AttachmentPath is null) throw new NotFoundException("Attachment", id);
        return (storage.OpenRead(n.AttachmentPath), n.AttachmentType ?? "application/octet-stream", n.AttachmentName ?? "attachment");
    }

    // ------------------------------------------------------------------ internals

    private async Task<bool> IsStaffAsync(CancellationToken ct) =>
        currentUser.IsSystem || (currentUser.UserId is { } me && await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == me && u.UserType == UserType.Staff, ct));

    private bool IsActive(Notice n) => n.ExpiresOn is null || n.ExpiresOn >= lookups.TodayDhaka;

    private async Task<(Guid CampusId, Guid? YearGroupId, bool Self)> TargetOfAsync(Guid studentId, CancellationToken ct)
    {
        var s = await db.Students.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == studentId).Select(x => new { x.CampusId, x.UserId }).FirstAsync(ct);
        var sectionId = await schoolDays.SectionOfAsync(studentId, lookups.TodayDhaka, ct);
        Guid? yearGroupId = sectionId is { } sid ? (await lookups.SectionAsync(sid, ct)).YearGroupId : null;
        return (s.CampusId, yearGroupId, s.UserId is not null && s.UserId == currentUser.UserId);
    }

    /// <summary>In-app to the targeted portal users and staff; urgent notices also by SMS to primary guardians.</summary>
    private async Task AnnounceAsync(Notice n, CancellationToken ct)
    {
        var text = n.Title + (n.Body.Length > 120 ? ": " + n.Body[..120] + "…" : ": " + n.Body);
        var priority = n.IsUrgent ? MessagePriority.High : MessagePriority.Normal;
        if (n.ForStudents || n.ForParents)
        {
            var today = lookups.TodayDhaka;
            var students = await db.StudentEnrolments.IgnoreQueryFilters().AsNoTracking()
                .Where(e => !e.IsDeleted && e.SectionId != null && e.StartDate <= today && (e.EndDate == null || e.EndDate >= today)
                            && (n.CampusId == null || e.CampusId == n.CampusId) && (n.YearGroupId == null || e.YearGroupId == n.YearGroupId))
                .Select(e => new { e.StudentId, e.CampusId, e.Student!.UserId }).Distinct().ToListAsync(ct);
            var ids = students.Select(s => s.StudentId).Distinct().ToList();
            if (n.ForStudents)
                foreach (var s in students.Where(s => s.UserId is not null).DistinctBy(s => s.UserId))
                    notifications.QueueRaw(NotificationChannel.InApp, s.UserId!.Value.ToString(), text, "Notice", priority, s.CampusId, s.UserId);
            if (n.ForParents)
            {
                var guardians = await db.StudentGuardians.IgnoreQueryFilters().AsNoTracking().Where(g => ids.Contains(g.StudentId) && !g.Guardian!.IsDeleted)
                    .Select(g => new { g.Guardian!.UserId, g.Guardian.Phone, g.IsPrimaryContact, g.Student!.CampusId }).ToListAsync(ct);
                foreach (var g in guardians.Where(g => g.UserId is not null).DistinctBy(g => g.UserId))
                    notifications.QueueRaw(NotificationChannel.InApp, g.UserId!.Value.ToString(), text, "Notice", priority, g.CampusId, g.UserId);
                if (n.IsUrgent)
                    foreach (var g in guardians.Where(g => g.IsPrimaryContact && !string.IsNullOrWhiteSpace(g.Phone)).DistinctBy(g => g.Phone))
                        notifications.QueueRaw(NotificationChannel.Sms, g.Phone, text, null, priority, g.CampusId);
            }
        }
        if (n.ForStaff)
        {
            var staff = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.UserType == UserType.Staff && (n.CampusId == null || u.RoleAssignments.Any(a => a.CampusId == null || a.CampusId == n.CampusId)))
                .Select(u => u.Id).ToListAsync(ct);
            foreach (var u in staff) notifications.QueueRaw(NotificationChannel.InApp, u.ToString(), text, "Notice", priority, n.CampusId, u);
        }
    }

    private void EnsureManage(Guid? campusId)
    {
        var ok = campusId is { } c ? currentUser.HasPermission(Permissions.Notices.Manage, c)
            : currentUser.HasPermission(Permissions.Notices.Manage) && (currentUser.IsSystem || currentUser.HasAllCampusAccess);
        if (!ok) throw new ForbiddenException(campusId is null ? "Notices for every campus need organisation-wide rights." : "You cannot post notices for this campus.");
    }

    private static void Validate(NoticeForm f)
    {
        if (!f.ForStudents && !f.ForParents && !f.ForStaff) throw new DomainException("Choose who the notice is for.", "no_audience");
    }

    private static void Apply(Notice n, NoticeForm f)
    {
        n.CampusId = f.CampusId;
        n.YearGroupId = f.YearGroupId;
        n.TitleBn = LearningText.Clean(f.TitleBn);
        n.ForStudents = f.ForStudents;
        n.ForParents = f.ForParents;
        n.ForStaff = f.ForStaff;
        n.IsPinned = f.IsPinned;
        n.IsUrgent = f.IsUrgent;
        n.ExpiresOn = f.ExpiresOn;
    }

    private async Task SaveAttachmentAsync(Notice n, IFormFile? file, CancellationToken ct)
    {
        if (file is not { Length: > 0 }) return;
        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(stream, "notices", file.FileName, file.ContentType, ct, allowOfficeDocuments: true);
        n.AttachmentPath = stored.Path;
        n.AttachmentName = stored.FileName;
        n.AttachmentType = stored.ContentType;
    }

    private async Task<List<NoticeView>> ViewsAsync(IReadOnlyList<Notice> rows, CancellationToken ct)
    {
        var campuses = await db.Campuses.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var classes = await db.YearGroups.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(y => y.Id, y => y.Name, ct);
        var names = await lookups.UserNamesAsync(rows.Select(r => r.CreatedBy), ct);
        return rows.Select(n => new NoticeView(n.Id, n.CampusId, n.CampusId is { } c ? campuses.GetValueOrDefault(c) : null, n.YearGroupId,
            n.YearGroupId is { } y ? classes.GetValueOrDefault(y) : null, n.Title, n.TitleBn, n.Body, n.ForStudents, n.ForParents, n.ForStaff, n.IsPinned, n.IsUrgent,
            n.PublishedAtUtc, n.ExpiresOn, n.AttachmentPath is not null, n.AttachmentName, n.CreatedBy is { } by ? names.GetValueOrDefault(by) : null, IsActive(n),
            n.CampusId is { } cc ? currentUser.HasPermission(Permissions.Notices.Manage, cc)
                : currentUser.HasPermission(Permissions.Notices.Manage) && (currentUser.IsSystem || currentUser.HasAllCampusAccess))).ToList();
    }
}

/// <summary>Notice board for staff (post/edit with notices.manage; read the staff board when signed in).</summary>
[ApiController]
[Route("api/notices")]
[Tags("Communication: Notices")]
[Authorize]
public sealed class NoticesController(NoticeService notices) : ControllerBase
{
    [HttpGet]
    public Task<List<NoticeView>> List([FromQuery] bool all = false, CancellationToken ct = default) => notices.ListAsync(all, ct);

    [HttpPost]
    [HasPermission(Permissions.Notices.Manage)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<NoticeView> Create([FromForm] NoticeForm form, CancellationToken ct) => notices.CreateAsync(form, ct);

    [HttpPost("{id:guid}")]
    [HasPermission(Permissions.Notices.Manage)]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<NoticeView> Update(Guid id, [FromForm] NoticeForm form, CancellationToken ct) => notices.UpdateAsync(id, form, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Notices.Manage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await notices.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/attachment")]
    public async Task<IActionResult> Attachment(Guid id, CancellationToken ct)
    {
        var (content, type, name) = await notices.AttachmentAsync(id, null, ct);
        return File(content, type, name);
    }
}

public sealed class NoticeFormValidator : AbstractValidator<NoticeForm>
{
    public NoticeFormValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TitleBn).MaximumLength(200);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(8000);
    }
}
