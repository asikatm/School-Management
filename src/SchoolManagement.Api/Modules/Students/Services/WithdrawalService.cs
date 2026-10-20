using System.Text.Json;
using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Approvals;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Approvals;
using SchoolManagement.Infrastructure.Numbering;

namespace SchoolManagement.Api.Modules.Students.Services;

public record ClearanceResult(string Area, bool Cleared, string Detail);

/// <summary>
/// One dues check for withdrawal (SIS-06). FEE, LIB, TRN and INV each register their own check when installed;
/// until then they report "not installed" so the TC is not blocked by modules the school does not use yet.
/// </summary>
public interface IClearanceCheck
{
    string Area { get; }
    Task<ClearanceResult> CheckAsync(Student student, CancellationToken ct);
}

public sealed class AdmissionDuesClearance(AppDbContext db) : IClearanceCheck
{
    public string Area => "Admission fees";

    public async Task<ClearanceResult> CheckAsync(Student student, CancellationToken ct)
    {
        if (student.ApplicationId is null) return new ClearanceResult(Area, true, "No admission invoice.");
        var due = await db.AdmissionInvoices.Where(i => i.ApplicationId == student.ApplicationId && i.Status == AdmissionInvoiceStatus.Unpaid)
            .SumAsync(i => (decimal?)i.Amount, ct) ?? 0;
        return due > 0
            ? new ClearanceResult(Area, false, $"BDT {Infrastructure.Localization.BanglaFormat.GroupLakhCrore(due)} unpaid.")
            : new ClearanceResult(Area, true, "Paid.");
    }
}

public sealed class PendingModuleClearance(string area) : IClearanceCheck
{
    public string Area => area;
    public Task<ClearanceResult> CheckAsync(Student student, CancellationToken ct) =>
        Task.FromResult(new ClearanceResult(area, true, "Module not installed yet; nothing to clear."));
}

public record WithdrawalCreateRequest(DateOnly LastAttendanceDate, string Reason, string? DestinationSchool);
public record WithdrawalCompleteRequest(string? Conduct, string? Remarks);

public sealed class WithdrawalService(
    AppDbContext db,
    IEnumerable<IClearanceCheck> checks,
    IApprovalService approvals,
    INumberSequenceService numbers,
    SelectionService selection,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<WithdrawalRequest> CreateAsync(Guid studentId, WithdrawalCreateRequest request, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Student", studentId);
        currentUser.EnsurePermission(Permissions.Students.Edit, student.CampusId);
        if (student.Status != StudentStatus.Active)
            throw new DomainException($"The student is {student.Status}.", "student_not_active", isConflict: true);
        if (await db.WithdrawalRequests.AnyAsync(w => w.StudentId == studentId && w.Status != WithdrawalStatus.Completed && w.Status != WithdrawalStatus.Rejected, ct))
            throw new DomainException("A withdrawal is already in progress for this student.", "withdrawal_exists", isConflict: true);

        var withdrawal = new WithdrawalRequest
        {
            StudentId = studentId, CampusId = student.CampusId, LastAttendanceDate = request.LastAttendanceDate,
            Reason = request.Reason, DestinationSchool = request.DestinationSchool
        };
        db.WithdrawalRequests.Add(withdrawal);
        await RunClearanceAsync(withdrawal, student, ct);

        var approval = await approvals.SubmitAsync(ApprovalTypes.Withdrawal, nameof(WithdrawalRequest), withdrawal.Id.ToString(), student.CampusId,
            $"{student.FullName} ({student.AdmissionNo}) — {request.Reason}", ct);
        if (approval is null)
            withdrawal.Status = withdrawal.Status == WithdrawalStatus.Cleared ? WithdrawalStatus.Approved : withdrawal.Status;
        else
            withdrawal.ApprovalRequestId = approval.Id;

        await db.SaveChangesAsync(ct);
        return withdrawal;
    }

    public async Task<WithdrawalRequest> RecheckAsync(Guid id, CancellationToken ct)
    {
        var w = await db.WithdrawalRequests.Include(x => x.Student).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Withdrawal", id);
        await RunClearanceAsync(w, w.Student!, ct);
        await db.SaveChangesAsync(ct);
        return w;
    }

    /// <summary>Issues the transfer certificate and closes the student's record. Requires approval and full clearance.</summary>
    public async Task<TransferCertificate> CompleteAsync(Guid id, WithdrawalCompleteRequest request, CancellationToken ct)
    {
        var w = await db.WithdrawalRequests.Include(x => x.Student).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Withdrawal", id);
        currentUser.EnsurePermission(Permissions.Students.Approve, w.CampusId);

        await RunClearanceAsync(w, w.Student!, ct);
        var clearance = JsonSerializer.Deserialize<List<ClearanceResult>>(w.ClearanceJson ?? "[]") ?? [];
        if (clearance.Any(c => !c.Cleared))
            throw new DomainException("Dues are not cleared: " + string.Join("; ", clearance.Where(c => !c.Cleared).Select(c => $"{c.Area}: {c.Detail}")), "not_cleared", isConflict: true);
        if (w.ApprovalRequestId is not null && w.Status != WithdrawalStatus.Approved)
            throw new DomainException("The withdrawal has not been approved yet.", "not_approved", isConflict: true);

        var student = w.Student!;
        var enrolment = await db.StudentEnrolments.FirstOrDefaultAsync(e => e.StudentId == student.Id && e.Status == EnrolmentStatus.Active, ct);
        var className = "-";
        if (enrolment is not null)
        {
            var yg = await db.YearGroups.Where(y => y.Id == enrolment.YearGroupId).Select(y => y.Name).FirstAsync(ct);
            var section = enrolment.SectionId is { } sid ? await db.Sections.Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct) : null;
            className = section is null ? yg : $"{yg} - {section}";
            enrolment.Status = EnrolmentStatus.Withdrawn;
            enrolment.EndDate = w.LastAttendanceDate;
        }

        var tc = new TransferCertificate
        {
            CertificateNo = await numbers.NextAsync(NumberKeys.TransferCertificate, w.CampusId, enrolment?.AcademicYearId, ct),
            CampusId = w.CampusId,
            StudentId = student.Id,
            WithdrawalRequestId = w.Id,
            IssueDate = DateOnly.FromDateTime(Infrastructure.Localization.BanglaFormat.ToDhaka(clock.GetUtcNow().UtcDateTime)),
            StudentName = student.FullName,
            LastClass = className,
            Conduct = request.Conduct,
            Remarks = request.Remarks
        };
        db.TransferCertificates.Add(tc);

        student.Status = StudentStatus.Withdrawn;
        student.LeftOn = w.LastAttendanceDate;
        student.CurrentEnrolmentId = null;
        await db.IdCards.Where(c => c.StudentId == student.Id && c.IsActive).ForEachAsync(c => c.IsActive = false, ct);

        w.Status = WithdrawalStatus.Completed;
        w.TransferCertificateId = tc.Id;
        await db.SaveChangesAsync(ct);

        // ADM-05: the freed seat goes to the head of the waiting list.
        if (enrolment is not null)
            await selection.PromoteWaitlistAsync(new ClassKey(enrolment.CampusId, enrolment.AcademicYearId, enrolment.YearGroupId), ct);
        return tc;
    }

    private async Task RunClearanceAsync(WithdrawalRequest w, Student student, CancellationToken ct)
    {
        var results = new List<ClearanceResult>();
        foreach (var check in checks) results.Add(await check.CheckAsync(student, ct));
        w.ClearanceJson = JsonSerializer.Serialize(results);
        if (w.Status is WithdrawalStatus.Requested or WithdrawalStatus.ClearancePending or WithdrawalStatus.Cleared)
            w.Status = results.All(r => r.Cleared) ? WithdrawalStatus.Cleared : WithdrawalStatus.ClearancePending;
    }
}

/// <summary>Moves a withdrawal forward when its approval workflow finishes (CORE-04 hook).</summary>
public sealed class WithdrawalApprovalHandler(AppDbContext db) : IApprovalHandler
{
    public string RequestType => ApprovalTypes.Withdrawal;

    public async Task OnApprovedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var w = await db.WithdrawalRequests.FirstOrDefaultAsync(x => x.Id == Guid.Parse(request.EntityId), ct);
        if (w is not null) w.Status = WithdrawalStatus.Approved;
    }

    public async Task OnRejectedAsync(ApprovalRequest request, CancellationToken ct)
    {
        var w = await db.WithdrawalRequests.FirstOrDefaultAsync(x => x.Id == Guid.Parse(request.EntityId), ct);
        if (w is not null) w.Status = WithdrawalStatus.Rejected;
    }
}
