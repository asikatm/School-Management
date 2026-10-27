using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Api.Modules.Attendance;
using SchoolManagement.Domain.Learning;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Api.Modules.Learning;

public sealed class ResourceForm
{
    public Guid? CampusId { get; init; }
    public Guid SubjectId { get; init; }
    public Guid? YearGroupId { get; init; }
    public Guid? SectionId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public string? Topic { get; init; }
    public ResourceType Type { get; init; }
    public ResourceAudience Audience { get; init; } = ResourceAudience.StudentsAndParents;
    public string? Url { get; init; }
    public IFormFile? File { get; init; }
}

public record ResourceView(Guid Id, Guid? CampusId, Guid SubjectId, string SubjectName, Guid? YearGroupId, string? ClassName, Guid? SectionId, string? SectionLabel,
    string Title, string? Description, string? Topic, ResourceType Type, ResourceAudience Audience, string? Url, bool HasFile, string? FileName, long? FileSize,
    int Downloads, string? SharedBy, DateTime SharedAtUtc, bool CanEdit);

public record ResourceDownloadView(string? User, DateTime AtUtc);

public record ResourceLink(string Url);

/// <summary>LRN-04: a shared library of notes, slides, videos and past papers by subject and class, with access control and download tracking.</summary>
public sealed class ResourceService(
    AppDbContext db,
    AcademicLookups lookups,
    SchoolDays schoolDays,
    ClassRoster roster,
    PortalAccess portal,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<List<ResourceView>> ListAsync(Guid? subjectId, Guid? yearGroupId, Guid? sectionId, ResourceType? type, string? search, CancellationToken ct)
    {
        var q = db.LearningResources.AsNoTracking();
        if (subjectId is not null) q = q.Where(r => r.SubjectId == subjectId);
        if (yearGroupId is not null) q = q.Where(r => r.YearGroupId == null || r.YearGroupId == yearGroupId);
        if (sectionId is not null) q = q.Where(r => r.SectionId == null || r.SectionId == sectionId);
        if (type is not null) q = q.Where(r => r.Type == type);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => r.Title.Contains(s) || (r.Topic != null && r.Topic.Contains(s)) || (r.Description != null && r.Description.Contains(s)));
        }
        var rows = await q.OrderByDescending(r => r.CreatedAtUtc).Take(500).ToListAsync(ct);
        return await ViewsAsync(rows.Where(r => r.CampusId is null || currentUser.CanAccessCampus(r.CampusId.Value)).ToList(), ct);
    }

    public async Task<ResourceView> CreateAsync(ResourceForm form, CancellationToken ct)
    {
        var campusId = await CampusOfAsync(form, ct);
        EnsureCanShare(campusId);
        var r = new LearningResource { Title = form.Title.Trim() };
        Apply(r, form, campusId);
        await SaveFileAsync(r, form.File, ct);
        if (r.FilePath is null && r.Url is null) throw new DomainException("Attach a file or give a link.", "file_or_link");
        db.LearningResources.Add(r);
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([r], ct))[0];
    }

    public async Task<ResourceView> UpdateAsync(Guid id, ResourceForm form, CancellationToken ct)
    {
        var r = await db.LearningResources.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Resource", id);
        EnsureCanEdit(r);
        var campusId = await CampusOfAsync(form, ct);
        EnsureCanShare(campusId);
        Apply(r, form, campusId);
        if (form.File is { Length: > 0 })
        {
            if (r.FilePath is not null) storage.Delete(r.FilePath);
            await SaveFileAsync(r, form.File, ct);
        }
        if (r.FilePath is null && r.Url is null) throw new DomainException("Attach a file or give a link.", "file_or_link");
        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([r], ct))[0];
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var r = await db.LearningResources.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Resource", id);
        EnsureCanEdit(r);
        db.LearningResources.Remove(r);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ResourceDownloadView>> DownloadsAsync(Guid id, CancellationToken ct)
    {
        var r = await db.LearningResources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Resource", id);
        EnsureStaffSees(r);
        var rows = await db.ResourceDownloads.AsNoTracking().Where(d => d.ResourceId == id).OrderByDescending(d => d.AtUtc).Take(500).ToListAsync(ct);
        var names = await lookups.UserNamesAsync(rows.Select(d => d.UserId), ct);
        return rows.Select(d => new ResourceDownloadView(d.UserId is { } u ? names.GetValueOrDefault(u) : null, d.AtUtc)).ToList();
    }

    /// <summary>Opens the file (staff, or a family whose child the resource is for) and records the download.</summary>
    public async Task<(Stream Content, string ContentType, string FileName)> OpenAsync(Guid id, Guid? studentId, CancellationToken ct)
    {
        var r = await LoadForAccessAsync(id, studentId, ct);
        if (r.FilePath is null) throw new NotFoundException("File", id);
        await TrackAsync(r, ct);
        return (storage.OpenRead(r.FilePath), r.FileType ?? "application/octet-stream", r.FileName ?? "resource");
    }

    /// <summary>Records a visit to a link (video, website) and returns it.</summary>
    public async Task<ResourceLink> VisitAsync(Guid id, Guid? studentId, CancellationToken ct)
    {
        var r = await LoadForAccessAsync(id, studentId, ct);
        if (r.Url is null) throw new NotFoundException("Link", id);
        await TrackAsync(r, ct);
        return new ResourceLink(r.Url);
    }

    /// <summary>Resources a student may use: shared with families, for their campus, class/section and subjects.</summary>
    public async Task<List<ResourceView>> ForStudentAsync(Guid studentId, Guid? subjectId, CancellationToken ct)
    {
        await portal.EnsureAsync(studentId, ct);
        var visible = await VisibleToStudentAsync(studentId, ct);
        if (subjectId is not null) visible = visible.Where(r => r.SubjectId == subjectId).ToList();
        return await ViewsAsync(visible, ct);
    }

    // ------------------------------------------------------------------ internals

    private async Task<List<LearningResource>> VisibleToStudentAsync(Guid studentId, CancellationToken ct)
    {
        var today = lookups.TodayDhaka;
        if (await schoolDays.SectionOfAsync(studentId, today, ct) is not { } sectionId) return [];
        var section = await lookups.SectionAsync(sectionId, ct);
        var rows = await db.LearningResources.IgnoreQueryFilters().AsNoTracking()
            .Where(r => !r.IsDeleted && r.Audience == ResourceAudience.StudentsAndParents && (r.CampusId == null || r.CampusId == section.CampusId)
                        && (r.YearGroupId == null || r.YearGroupId == section.YearGroupId) && (r.SectionId == null || r.SectionId == section.Id))
            .OrderByDescending(r => r.CreatedAtUtc).ToListAsync(ct);
        var result = new List<LearningResource>();
        foreach (var g in rows.GroupBy(r => r.SubjectId))
            if ((await roster.TakersAsync(section.YearGroupId, section.AcademicYearId, g.Key, [studentId], ct)).Count > 0) result.AddRange(g);
        return result;
    }

    private async Task<LearningResource> LoadForAccessAsync(Guid id, Guid? studentId, CancellationToken ct)
    {
        var r = await db.LearningResources.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct) ?? throw new NotFoundException("Resource", id);
        if (studentId is { } sid)
        {
            await portal.EnsureAsync(sid, ct);
            if ((await VisibleToStudentAsync(sid, ct)).All(x => x.Id != id)) throw new ForbiddenException("This resource is not shared with your class.");
        }
        else EnsureStaffSees(r);
        return r;
    }

    private async Task TrackAsync(LearningResource r, CancellationToken ct)
    {
        r.Downloads++;
        db.ResourceDownloads.Add(new ResourceDownload { ResourceId = r.Id, UserId = currentUser.UserId, AtUtc = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(ct);
    }

    private void EnsureStaffSees(LearningResource r)
    {
        var ok = r.CampusId is { } c ? currentUser.HasPermission(Permissions.Learning.View, c) : currentUser.HasPermission(Permissions.Learning.View);
        if (!ok) throw new ForbiddenException("You cannot open this resource.");
    }

    private void EnsureCanShare(Guid? campusId)
    {
        var ok = campusId is { } c
            ? currentUser.HasPermission(Permissions.Learning.Teach, c) || currentUser.HasPermission(Permissions.Learning.Manage, c)
            : currentUser.HasPermission(Permissions.Learning.Manage);
        if (!ok) throw new ForbiddenException(campusId is null ? "Sharing with every campus needs learning.manage." : "You cannot share resources on this campus.");
    }

    private void EnsureCanEdit(LearningResource r)
    {
        var manager = r.CampusId is { } c ? currentUser.HasPermission(Permissions.Learning.Manage, c) : currentUser.HasPermission(Permissions.Learning.Manage);
        if (!manager && r.CreatedBy != currentUser.UserId) throw new ForbiddenException("Only the teacher who shared this resource can change it.");
    }

    /// <summary>A section fixes the campus and class; otherwise the form's campus (null = every campus).</summary>
    private async Task<Guid?> CampusOfAsync(ResourceForm form, CancellationToken ct)
    {
        if (form.SectionId is { } sid) return (await lookups.SectionAsync(sid, ct)).CampusId;
        return form.CampusId;
    }

    private static void Apply(LearningResource r, ResourceForm form, Guid? campusId)
    {
        if (form.Url is { Length: > 0 } url && !(Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            throw new DomainException("The link must be a full http(s) address.", "bad_url");
        r.CampusId = campusId;
        r.SubjectId = form.SubjectId;
        r.YearGroupId = form.YearGroupId;
        r.SectionId = form.SectionId;
        r.Title = form.Title.Trim();
        r.Description = LearningText.Clean(form.Description);
        r.Topic = LearningText.Clean(form.Topic);
        r.Type = form.Type;
        r.Audience = form.Audience;
        r.Url = LearningText.Clean(form.Url);
    }

    private async Task SaveFileAsync(LearningResource r, IFormFile? file, CancellationToken ct)
    {
        if (file is not { Length: > 0 }) return;
        await using var stream = file.OpenReadStream();
        var stored = await storage.SaveAsync(stream, $"resources/{r.SubjectId:N}", file.FileName, file.ContentType, ct, allowOfficeDocuments: true);
        r.FilePath = stored.Path;
        r.FileName = stored.FileName;
        r.FileType = stored.ContentType;
        r.FileSize = stored.SizeBytes;
    }

    private async Task<List<ResourceView>> ViewsAsync(IReadOnlyList<LearningResource> rows, CancellationToken ct)
    {
        var subjects = await lookups.SubjectsAsync(ct);
        var classes = await db.YearGroups.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(y => y.Id, y => y.Name, ct);
        var sections = await lookups.SectionsAsync(rows.Select(r => r.SectionId).OfType<Guid>(), ct);
        var names = await lookups.UserNamesAsync(rows.Select(r => r.CreatedBy), ct);
        return rows.Select(r =>
        {
            var manager = r.CampusId is { } c ? currentUser.HasPermission(Permissions.Learning.Manage, c) : currentUser.HasPermission(Permissions.Learning.Manage);
            return new ResourceView(r.Id, r.CampusId, r.SubjectId, subjects.GetValueOrDefault(r.SubjectId).Name ?? "?", r.YearGroupId,
                r.YearGroupId is { } y ? classes.GetValueOrDefault(y) : null, r.SectionId, r.SectionId is { } s ? sections.GetValueOrDefault(s)?.Label : null,
                r.Title, r.Description, r.Topic, r.Type, r.Audience, r.Url, r.FilePath is not null, r.FileName, r.FileSize, r.Downloads,
                r.CreatedBy is { } by ? names.GetValueOrDefault(by) : null, r.CreatedAtUtc, manager || (r.CreatedBy is not null && r.CreatedBy == currentUser.UserId));
        }).ToList();
    }
}
