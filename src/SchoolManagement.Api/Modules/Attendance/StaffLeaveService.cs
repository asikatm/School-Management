using SchoolManagement.Api.Modules.Academics;
using SchoolManagement.Domain.Approvals;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Infrastructure.Approvals;

namespace SchoolManagement.Api.Modules.Attendance;

public record LeaveTypeRequest(string Code, string Name, string? NameBn, decimal DaysPerYear, bool IsPaid, bool RequiresDocument, bool IsActive);

public record LeaveTypeView(Guid Id, string Code, string Name, string? NameBn, decimal DaysPerYear, bool IsPaid, bool RequiresDocument, bool IsActive);

public record EntitlementRequest(Guid UserId, Guid LeaveTypeId, Guid AcademicYearId, decimal Days);

public record LeaveBalance(Guid LeaveTypeId, string Code, string Name, bool Unlimited, decimal Entitled, decimal Taken, decimal Pending, decimal Remaining);

public record StaffLeaveApply(Guid LeaveTypeId, DateOnly FromDate, DateOnly ToDate, bool HalfDay, string Reason, Guid? CampusId);

public record StaffLeaveView(Guid Id, Guid UserId, string Name, Guid CampusId, Guid LeaveTypeId, string LeaveTypeCode, string LeaveType, DateOnly FromDate, DateOnly ToDate,
    bool HalfDay, decimal Days, string Reason, LeaveStatus Status, Guid? ApprovalRequestId, string? DecisionNote, DateTime RequestedAtUtc);

/// <summary>
/// ATT-07: leave types with yearly entitlements (overridable per person), balances, and requests that go through the
/// CORE-04 approval chain (Head of Section, escalating to the Principal). Approved days show as Leave on the staff register
/// and take the teacher out of the cover list (ACA-05).
/// </summary>
public sealed class StaffLeaveService(
    AppDbContext db,
    AcademicLookups lookups,
    SchoolDays schoolDays,
    StaffAttendanceService staffAttendance,
    IApprovalService approvals,
    ICurrentUser currentUser)
{
    // ---- Leave types and entitlements

    public async Task<List<LeaveTypeView>> TypesAsync(bool activeOnly, CancellationToken ct) =>
        await db.LeaveTypes.AsNoTracking().Where(t => !activeOnly || t.IsActive).OrderBy(t => t.Code)
            .Select(t => new LeaveTypeView(t.Id, t.Code, t.Name, t.NameBn, t.DaysPerYear, t.IsPaid, t.RequiresDocument, t.IsActive)).ToListAsync(ct);

    public async Task<LeaveTypeView> SaveTypeAsync(Guid? id, LeaveTypeRequest r, CancellationToken ct)
    {
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.LeaveTypes.IgnoreQueryFilters().AnyAsync(t => t.Code == code && t.Id != id, ct))
            throw new DomainException($"Leave type code {code} is already used.", "duplicate_code", isConflict: true);
        LeaveType type;
        if (id is { } existing) type = await db.LeaveTypes.FirstOrDefaultAsync(t => t.Id == existing, ct) ?? throw new NotFoundException("Leave type", existing);
        else db.LeaveTypes.Add(type = new LeaveType { Code = code, Name = r.Name });
        type.Code = code;
        type.Name = r.Name.Trim();
        type.NameBn = string.IsNullOrWhiteSpace(r.NameBn) ? null : r.NameBn.Trim();
        type.DaysPerYear = r.DaysPerYear;
        type.IsPaid = r.IsPaid;
        type.RequiresDocument = r.RequiresDocument;
        type.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        return new LeaveTypeView(type.Id, type.Code, type.Name, type.NameBn, type.DaysPerYear, type.IsPaid, type.RequiresDocument, type.IsActive);
    }

    public async Task SetEntitlementAsync(EntitlementRequest r, CancellationToken ct)
    {
        if (r.Days < 0) throw new DomainException("Days cannot be negative.", "bad_days");
        var row = await db.LeaveEntitlements.FirstOrDefaultAsync(e => e.UserId == r.UserId && e.LeaveTypeId == r.LeaveTypeId && e.AcademicYearId == r.AcademicYearId, ct);
        if (row is null) db.LeaveEntitlements.Add(new LeaveEntitlement { UserId = r.UserId, LeaveTypeId = r.LeaveTypeId, AcademicYearId = r.AcademicYearId, Days = r.Days });
        else row.Days = r.Days;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<LeaveBalance>> BalancesAsync(Guid userId, Guid? yearId, CancellationToken ct)
    {
        if (userId != currentUser.UserId) currentUser.EnsurePermission(Permissions.StaffLeave.Manage);
        var year = yearId ?? await schoolDays.YearForAsync(lookups.TodayDhaka, ct);
        var types = await db.LeaveTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Code).ToListAsync(ct);
        var overrides = await db.LeaveEntitlements.AsNoTracking().Where(e => e.UserId == userId && e.AcademicYearId == year).ToDictionaryAsync(e => e.LeaveTypeId, e => e.Days, ct);
        var used = await db.StaffLeaveRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(l => !l.IsDeleted && l.UserId == userId && l.AcademicYearId == year && (l.Status == LeaveStatus.Approved || l.Status == LeaveStatus.Pending))
            .Select(l => new { l.LeaveTypeId, l.Status, l.Days }).ToListAsync(ct);
        return types.Select(t =>
        {
            var entitled = overrides.TryGetValue(t.Id, out var d) ? d : t.DaysPerYear;
            var unlimited = entitled == 0 && !overrides.ContainsKey(t.Id);
            var taken = used.Where(u => u.LeaveTypeId == t.Id && u.Status == LeaveStatus.Approved).Sum(u => u.Days);
            var pending = used.Where(u => u.LeaveTypeId == t.Id && u.Status == LeaveStatus.Pending).Sum(u => u.Days);
            return new LeaveBalance(t.Id, t.Code, t.Name, unlimited, entitled, taken, pending, unlimited ? 0 : entitled - taken - pending);
        }).ToList();
    }

    // ---- Requests

    public async Task<StaffLeaveView> ApplyAsync(StaffLeaveApply r, CancellationToken ct)
    {
        var me = currentUser.UserId ?? throw new ForbiddenException("Sign in to apply for leave.");
        var type = await db.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == r.LeaveTypeId && t.IsActive, ct) ?? throw new NotFoundException("Leave type", r.LeaveTypeId);
        if (r.ToDate < r.FromDate) throw new DomainException("The leave must end on or after its first day.", "bad_range");
        if (r.HalfDay && r.FromDate != r.ToDate) throw new DomainException("A half day must be a single date.", "half_day_range");
        if (string.IsNullOrWhiteSpace(r.Reason)) throw new DomainException("Give a reason for the leave.", "reason_required");
        var campusId = r.CampusId ?? await staffAttendance.HomeCampusAsync(me, ct)
                       ?? throw new DomainException("You work at more than one campus; choose the campus for this leave.", "campus_required");

        var yearId = await schoolDays.YearForAsync(r.FromDate, ct);
        if (await schoolDays.YearForAsync(r.ToDate, ct) != yearId)
            throw new DomainException("Leave cannot run across two academic years; apply for each year separately.", "crosses_year");
        var days = (await schoolDays.BetweenAsync(campusId, r.FromDate, r.ToDate, ct)).Count;
        if (days == 0) throw new DomainException("There are no working days in that range.", "no_working_days");
        var count = r.HalfDay ? 0.5m : days;

        if (await db.StaffLeaveRequests.AnyAsync(l => l.UserId == me && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)
                                                      && l.FromDate <= r.ToDate && l.ToDate >= r.FromDate, ct))
            throw new DomainException("You already have leave on some of these days.", "leave_overlaps", isConflict: true);
        var balance = (await BalancesAsync(me, yearId, ct)).First(b => b.LeaveTypeId == type.Id);
        if (!balance.Unlimited && count > balance.Remaining)
            throw new DomainException($"Not enough {type.Name.ToLowerInvariant()} left: {balance.Remaining:0.#} day(s) remaining, {count:0.#} requested.", "insufficient_balance");

        var leave = new StaffLeaveRequest
        {
            CampusId = campusId, AcademicYearId = yearId, UserId = me, LeaveTypeId = type.Id, FromDate = r.FromDate, ToDate = r.ToDate, HalfDay = r.HalfDay,
            Days = count, Reason = r.Reason.Trim(), Status = LeaveStatus.Pending
        };
        db.StaffLeaveRequests.Add(leave);

        var name = (await lookups.UserNamesAsync([me], ct)).GetValueOrDefault(me);
        var approval = await approvals.SubmitAsync(ApprovalTypes.StaffLeave, nameof(StaffLeaveRequest), leave.Id.ToString(), campusId,
            $"{name}: {type.Name}, {r.FromDate:dd MMM}{(r.ToDate != r.FromDate ? $" – {r.ToDate:dd MMM yyyy}" : $" {r.FromDate:yyyy}")} ({count:0.#} day(s)) — {leave.Reason}", ct);
        if (approval is null)
        {
            // No workflow configured: leave is granted as applied.
            leave.Status = LeaveStatus.Approved;
            await MarkLeaveDaysAsync(db, schoolDays, leave, ct);
        }
        else leave.ApprovalRequestId = approval.Id;

        await db.SaveChangesAsync(ct);
        return (await ViewsAsync([leave], ct))[0];
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var leave = await db.StaffLeaveRequests.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == id && !l.IsDeleted, ct) ?? throw new NotFoundException("Leave request", id);
        if (leave.UserId != currentUser.UserId) currentUser.EnsurePermission(Permissions.StaffLeave.Manage, leave.CampusId);
        if (leave.Status is LeaveStatus.Rejected or LeaveStatus.Cancelled) throw new DomainException("This request is already closed.", "leave_closed", isConflict: true);
        if (leave.Status == LeaveStatus.Approved && leave.FromDate <= lookups.TodayDhaka)
            throw new DomainException("The leave has already started; ask the attendance office to correct the register.", "leave_started", isConflict: true);

        if (leave.ApprovalRequestId is { } requestId
            && await db.ApprovalRequests.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == requestId && a.Status == ApprovalStatus.Pending, ct) is { } request)
            request.Status = ApprovalStatus.Cancelled;
        if (leave.Status == LeaveStatus.Approved)
            db.StaffAttendances.RemoveRange(await db.StaffAttendances.IgnoreQueryFilters().Where(a => a.StaffLeaveRequestId == leave.Id && a.CheckInUtc == null).ToListAsync(ct));
        leave.Status = LeaveStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<StaffLeaveView>> MineAsync(CancellationToken ct)
    {
        var me = currentUser.UserId ?? throw new ForbiddenException("Sign in first.");
        var rows = await db.StaffLeaveRequests.IgnoreQueryFilters().AsNoTracking().Include(l => l.LeaveType)
            .Where(l => !l.IsDeleted && l.UserId == me).OrderByDescending(l => l.FromDate).Take(200).ToListAsync(ct);
        return await ViewsAsync(rows, ct);
    }

    public async Task<List<StaffLeaveView>> ListAsync(LeaveStatus? status, Guid? campusId, Guid? userId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var q = db.StaffLeaveRequests.AsNoTracking().Include(l => l.LeaveType).AsQueryable();
        if (status is not null) q = q.Where(l => l.Status == status);
        if (campusId is not null) q = q.Where(l => l.CampusId == campusId);
        if (userId is not null) q = q.Where(l => l.UserId == userId);
        if (from is not null) q = q.Where(l => l.ToDate >= from);
        if (to is not null) q = q.Where(l => l.FromDate <= to);
        return await ViewsAsync(await q.OrderBy(l => l.FromDate).Take(1000).ToListAsync(ct), ct);
    }

    /// <summary>Approved staff leave on a day — those teachers are not offered as cover (ACA-05).</summary>
    public static IQueryable<Guid> OnLeave(AppDbContext db, DateOnly date) =>
        db.StaffLeaveRequests.IgnoreQueryFilters()
            .Where(l => !l.IsDeleted && l.Status == LeaveStatus.Approved && l.FromDate <= date && l.ToDate >= date)
            .Select(l => l.UserId);

    // ------------------------------------------------------------------ internals

    /// <summary>Approved leave shows as Leave on the staff register for each working day it covers (days with a punch are kept).</summary>
    internal static async Task MarkLeaveDaysAsync(AppDbContext db, SchoolDays schoolDays, StaffLeaveRequest leave, CancellationToken ct)
    {
        var days = await schoolDays.BetweenAsync(leave.CampusId, leave.FromDate, leave.ToDate, ct);
        var existing = await db.StaffAttendances.IgnoreQueryFilters().Where(a => a.UserId == leave.UserId && a.Date >= leave.FromDate && a.Date <= leave.ToDate)
            .ToDictionaryAsync(a => a.Date, ct);
        foreach (var day in days)
        {
            if (existing.TryGetValue(day, out var row))
            {
                if (row.CheckInUtc is not null) continue;
            }
            else
            {
                row = new StaffAttendance { CampusId = leave.CampusId, UserId = leave.UserId, Date = day };
                db.StaffAttendances.Add(row);
            }
            row.Status = StaffAttendanceStatus.Leave;
            row.Source = AttendanceSource.Leave;
            row.StaffLeaveRequestId = leave.Id;
            row.LateMinutes = row.EarlyLeaveMinutes = row.OvertimeMinutes = 0;
            row.Note = leave.HalfDay ? "Half-day leave" : null;
        }
    }

    private async Task<List<StaffLeaveView>> ViewsAsync(IReadOnlyList<StaffLeaveRequest> rows, CancellationToken ct)
    {
        var names = await lookups.UserNamesAsync(rows.Select(r => (Guid?)r.UserId), ct);
        var typeIds = rows.Select(r => r.LeaveTypeId).Distinct().ToList();
        var types = await db.LeaveTypes.IgnoreQueryFilters().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        return rows.Select(r =>
        {
            var t = types.GetValueOrDefault(r.LeaveTypeId);
            return new StaffLeaveView(r.Id, r.UserId, names.GetValueOrDefault(r.UserId, "?"), r.CampusId, r.LeaveTypeId, t?.Code ?? "?", t?.Name ?? "?", r.FromDate, r.ToDate,
                r.HalfDay, r.Days, r.Reason, r.Status, r.ApprovalRequestId, r.DecisionNote, r.CreatedAtUtc);
        }).ToList();
    }
}

/// <summary>CORE-04 hook: the staff leave chain's final decision updates the request and the staff register.</summary>
public sealed class StaffLeaveApprovalHandler(AppDbContext db, SchoolDays schoolDays) : IApprovalHandler
{
    public string RequestType => ApprovalTypes.StaffLeave;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        if (await FindAsync(request, ct) is not { } leave) return;
        leave.Status = LeaveStatus.Approved;
        leave.DecisionNote = LastComment(request);
        await StaffLeaveService.MarkLeaveDaysAsync(db, schoolDays, leave, ct);
    }

    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        if (await FindAsync(request, ct) is not { } leave) return;
        leave.Status = LeaveStatus.Rejected;
        leave.DecisionNote = LastComment(request);
    }

    private Task<StaffLeaveRequest?> FindAsync(ApprovalRequest request, CancellationToken ct) =>
        db.StaffLeaveRequests.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.Id == Guid.Parse(request.EntityId) && l.Status == LeaveStatus.Pending, ct);

    private static string? LastComment(ApprovalRequest request) =>
        request.Decisions.Where(d => d.Decision != ApprovalDecisionType.Escalated).OrderBy(d => d.DecidedAtUtc).LastOrDefault()?.Comment;
}
