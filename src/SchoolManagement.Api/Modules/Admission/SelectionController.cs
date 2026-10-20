using SchoolManagement.Api.Modules.Admission.Services;
using SchoolManagement.Domain.Admission;

namespace SchoolManagement.Api.Modules.Admission;

/// <summary>ADM-05: seat capacity &amp; availability dashboard, rule-based merit list, batch offers and the waiting list.</summary>
[ApiController]
[Route("api/admission/selection")]
[Tags("Admission: Selection")]
public sealed class SelectionController(AppDbContext db, SelectionService selection, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Seat availability by class for one campus and year.</summary>
    [HttpGet("seats")]
    [HasPermission(Permissions.Admission.View)]
    public Task<List<SeatSummary>> Seats([FromQuery] Guid campusId, [FromQuery] Guid academicYearId, CancellationToken ct)
    {
        if (!currentUser.CanAccessCampus(campusId)) throw new ForbiddenException();
        return selection.SeatsAsync(campusId, academicYearId, ct);
    }

    /// <summary>Sets the number of seats offered for a class. Without a value, the sum of section capacities is used.</summary>
    [HttpPut("seats")]
    [HasPermission(Permissions.Admission.Approve)]
    public async Task<SeatSummary> SetSeats(SeatRequest r, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Admission.Approve, r.CampusId);
        var seat = await db.SeatCapacities.FirstOrDefaultAsync(s => s.CampusId == r.CampusId && s.AcademicYearId == r.AcademicYearId && s.YearGroupId == r.YearGroupId, ct);
        if (seat is null)
        {
            seat = new SeatCapacity { CampusId = r.CampusId, AcademicYearId = r.AcademicYearId, YearGroupId = r.YearGroupId };
            db.SeatCapacities.Add(seat);
        }
        seat.TotalSeats = r.TotalSeats;
        await db.SaveChangesAsync(ct);
        var key = new ClassKey(r.CampusId, r.AcademicYearId, r.YearGroupId);
        await selection.PromoteWaitlistAsync(key, ct);
        return await selection.SeatAsync(key, null, ct);
    }

    /// <summary>Recomputes merit scores and ranks for every tested applicant in the class (age cut-off, weights, priorities).</summary>
    [HttpPost("merit-list")]
    [HasPermission(Permissions.Admission.Approve)]
    public Task<List<MeritEntry>> ComputeMeritList(ClassKey key, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Admission.Approve, key.CampusId);
        return selection.ComputeMeritAsync(key, ct);
    }

    /// <summary>Offers seats to the top of the merit list and wait-lists the rest of the shortlisted applicants.</summary>
    [HttpPost("merit-list/offers")]
    [HasPermission(Permissions.Admission.Approve)]
    public async Task<BatchOfferResult> OfferFromMeritList(BatchOfferRequest r, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Admission.Approve, r.CampusId);
        var (offers, waitlisted) = await selection.OfferFromMeritListAsync(new ClassKey(r.CampusId, r.AcademicYearId, r.YearGroupId), r.MaxOffers,
            new OfferOptions(r.ExpiresInDays, r.FeeScheduleText, r.FeeLines, r.Language), ct);
        return new BatchOfferResult(offers.Select(o => new OfferedItem(o.ApplicationId, o.OfferNo, o.ExpiresAtUtc)).ToList(),
            waitlisted.Select(w => new WaitlistItem(w.ApplicationId, w.Position)).ToList());
    }

    [HttpGet("waitlist")]
    [HasPermission(Permissions.Admission.View)]
    public async Task<List<WaitlistView>> Waitlist([FromQuery] Guid campusId, [FromQuery] Guid academicYearId, [FromQuery] Guid yearGroupId, CancellationToken ct) =>
        await db.WaitlistEntries.AsNoTracking()
            .Where(w => w.CampusId == campusId && w.AcademicYearId == academicYearId && w.YearGroupId == yearGroupId && w.IsActive)
            .OrderBy(w => w.Position)
            .Select(w => new WaitlistView(w.Id, w.Position, w.ApplicationId, w.Application!.ApplicationNo, w.Application.FirstName + " " + (w.Application.LastName ?? ""),
                w.Application.MeritScore, w.Application.MeritRank, w.CreatedAtUtc))
            .ToListAsync(ct);

    /// <summary>Offers any free seats to the head of the waiting list now (also runs automatically when seats are released).</summary>
    [HttpPost("waitlist/promote")]
    [HasPermission(Permissions.Admission.Approve)]
    public async Task<int> PromoteWaitlist(ClassKey key, CancellationToken ct)
    {
        currentUser.EnsurePermission(Permissions.Admission.Approve, key.CampusId);
        return await selection.PromoteWaitlistAsync(key, ct);
    }
}

public record SeatRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, int TotalSeats);
public record BatchOfferRequest(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, int? MaxOffers, int? ExpiresInDays, string? FeeScheduleText, List<FeeLine>? FeeLines, string? Language);
public record OfferedItem(Guid ApplicationId, string OfferNo, DateTime ExpiresAtUtc);
public record WaitlistItem(Guid ApplicationId, int Position);
public record BatchOfferResult(IReadOnlyList<OfferedItem> Offered, IReadOnlyList<WaitlistItem> Waitlisted);
public record WaitlistView(Guid Id, int Position, Guid ApplicationId, string ApplicationNo, string Name, decimal? MeritScore, int? MeritRank, DateTime AddedAtUtc);

public sealed class SeatRequestValidator : AbstractValidator<SeatRequest>
{
    public SeatRequestValidator() => RuleFor(x => x.TotalSeats).InclusiveBetween(0, 2000);
}

public sealed class BatchOfferRequestValidator : AbstractValidator<BatchOfferRequest>
{
    public BatchOfferRequestValidator()
    {
        RuleFor(x => x.MaxOffers).GreaterThan(0).When(x => x.MaxOffers is not null);
        RuleFor(x => x.ExpiresInDays).InclusiveBetween(1, 60).When(x => x.ExpiresInDays is not null);
        RuleForEach(x => x.FeeLines).ChildRules(l =>
        {
            l.RuleFor(x => x.Description).NotEmpty().MaximumLength(200);
            l.RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        });
    }
}
