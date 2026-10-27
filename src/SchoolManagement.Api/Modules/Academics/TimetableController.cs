namespace SchoolManagement.Api.Modules.Academics;

/// <summary>ACA-04 timetable builder and ACA-08 daily routines (section, teacher, room and "my day").</summary>
[ApiController]
[Route("api/academics/timetable")]
[Tags("Academics: Timetable")]
public sealed class TimetableController(AppDbContext db, TimetableService timetable, AcademicLookups lookups, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("section/{sectionId:guid}")]
    [HasPermission(Permissions.Academic.View)]
    public Task<SectionTimetable> Section(Guid sectionId, CancellationToken ct) => timetable.GetSectionAsync(sectionId, ct);

    [HttpGet("teacher/{userId:guid}")]
    [HasPermission(Permissions.Academic.View)]
    public Task<PersonTimetable> Teacher(Guid userId, [FromQuery] Guid academicYearId, CancellationToken ct) => timetable.TeacherAsync(userId, academicYearId, ct);

    [HttpGet("room/{roomId:guid}")]
    [HasPermission(Permissions.Academic.View)]
    public Task<PersonTimetable> Room(Guid roomId, [FromQuery] Guid academicYearId, CancellationToken ct) => timetable.RoomAsync(roomId, academicYearId, ct);

    /// <summary>Places (or changes) a lesson in a cell. Set Parallel to add an elective beside others of its option group.</summary>
    [HttpPut("slot")]
    [HasPermission(Permissions.Academic.Timetable)]
    public Task<SlotSaveResult> SaveSlot(SlotRequest request, CancellationToken ct) => timetable.SaveSlotAsync(request, ct);

    [HttpDelete("slot/{id:guid}")]
    [HasPermission(Permissions.Academic.Timetable)]
    public async Task<IActionResult> DeleteSlot(Guid id, CancellationToken ct)
    {
        await timetable.DeleteSlotAsync(id, ct);
        return NoContent();
    }

    [HttpDelete("section/{sectionId:guid}")]
    [HasPermission(Permissions.Academic.Timetable)]
    public Task<int> ClearSection(Guid sectionId, CancellationToken ct) => timetable.ClearSectionAsync(sectionId, ct);

    [HttpPost("section/{sectionId:guid}/auto")]
    [HasPermission(Permissions.Academic.Timetable)]
    public Task<AutoTimetableResult> Auto(Guid sectionId, AutoTimetableRequest request, CancellationToken ct) => timetable.AutoAsync(sectionId, request, ct);

    /// <summary>Clashes and unplaced periods for a year (optionally one campus).</summary>
    [HttpGet("check")]
    [HasPermission(Permissions.Academic.View)]
    public Task<IReadOnlyList<TimetableIssue>> Check([FromQuery] Guid academicYearId, [FromQuery] Guid? campusId, CancellationToken ct) =>
        timetable.CheckAsync(academicYearId, campusId, ct);

    /// <summary>The signed-in teacher's day: own periods, cover given or received, and class-teacher sections.</summary>
    [HttpGet("my-day")]
    [Authorize]
    public Task<TimetableService.MyDay> MyDay([FromQuery] DateOnly? date, CancellationToken ct) =>
        timetable.MyDayAsync(currentUser.UserId!.Value, date ?? lookups.TodayDhaka, ct);

    [HttpGet("my-week")]
    [Authorize]
    public async Task<PersonTimetable> MyWeek([FromQuery] Guid? academicYearId, CancellationToken ct)
    {
        var year = academicYearId ?? await db.AcademicYears.Where(y => y.IsCurrent).Select(y => (Guid?)y.Id).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Academic year", "current");
        return await timetable.TeacherAsync(currentUser.UserId!.Value, year, ct);
    }

    [HttpGet("section/{sectionId:guid}/print")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<ContentResult> PrintSection(Guid sectionId, CancellationToken ct) =>
        Content(timetable.RenderSectionHtml(await timetable.GetSectionAsync(sectionId, ct), await SchoolAsync(ct)), "text/html; charset=utf-8");

    [HttpGet("teacher/{userId:guid}/print")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<ContentResult> PrintTeacher(Guid userId, [FromQuery] Guid academicYearId, CancellationToken ct) =>
        Content(timetable.RenderPersonHtml(await timetable.TeacherAsync(userId, academicYearId, ct), "Teacher routine", await SchoolAsync(ct)), "text/html; charset=utf-8");

    [HttpGet("room/{roomId:guid}/print")]
    [HasPermission(Permissions.Academic.View)]
    public async Task<ContentResult> PrintRoom(Guid roomId, [FromQuery] Guid academicYearId, CancellationToken ct) =>
        Content(timetable.RenderPersonHtml(await timetable.RoomAsync(roomId, academicYearId, ct), "Room routine", await SchoolAsync(ct)), "text/html; charset=utf-8");

    private async Task<string> SchoolAsync(CancellationToken ct) => await db.Organisations.Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "School";
}

public sealed class SlotRequestValidator : AbstractValidator<SlotRequest>
{
    public SlotRequestValidator()
    {
        RuleFor(x => x.PeriodNo).InclusiveBetween(1, 20);
        RuleFor(x => x.Note).MaximumLength(200);
    }
}
