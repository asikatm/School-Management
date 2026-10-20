using System.Security.Cryptography;
using System.Text.Json;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Numbering;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Admission.Services;

public record ClassKey(Guid CampusId, Guid AcademicYearId, Guid YearGroupId);

public record SeatSummary(Guid CampusId, Guid AcademicYearId, Guid YearGroupId, string YearGroupName, int TotalSeats,
    int Enrolled, int AcceptedAwaitingEnrolment, int OffersPending, int Available, int Waitlisted);

public record MeritEntry(Guid ApplicationId, string ApplicationNo, string Name, DateOnly DateOfBirth, decimal? TestScore, decimal? InterviewScore,
    bool Sibling, bool StaffWard, decimal? MeritScore, int? Rank, bool Eligible, IReadOnlyList<string> Notes, ApplicationStatus Status);

public record FeeLine(Guid? FeeHeadId, string Description, decimal Amount);

public record OfferOptions(int? ExpiresInDays, string? FeeScheduleText, List<FeeLine>? FeeLines, string? Language);

/// <summary>ADM-05 / ADM-06: seats, rule-based merit list, offers, waiting list and automatic seat release.</summary>
public sealed class SelectionService(
    AppDbContext db,
    ISettingsService settings,
    INumberSequenceService numbers,
    AdmissionNotifier notifier,
    TimeProvider clock,
    IConfiguration configuration)
{
    public async Task<List<SeatSummary>> SeatsAsync(Guid campusId, Guid academicYearId, CancellationToken ct)
    {
        var yearGroups = await db.YearGroups.AsNoTracking().OrderBy(y => y.Sequence).Select(y => new { y.Id, y.Name }).ToListAsync(ct);
        var result = new List<SeatSummary>();
        foreach (var yg in yearGroups)
            result.Add(await SeatAsync(new ClassKey(campusId, academicYearId, yg.Id), yg.Name, ct));
        return result;
    }

    public async Task<SeatSummary> SeatAsync(ClassKey key, string? yearGroupName, CancellationToken ct)
    {
        var total = await db.SeatCapacities.Where(s => s.CampusId == key.CampusId && s.AcademicYearId == key.AcademicYearId && s.YearGroupId == key.YearGroupId)
            .Select(s => (int?)s.TotalSeats).FirstOrDefaultAsync(ct);
        total ??= await db.Sections.Where(s => s.CampusId == key.CampusId && s.AcademicYearId == key.AcademicYearId && s.YearGroupId == key.YearGroupId)
            .SumAsync(s => (int?)s.Capacity, ct) ?? 0;

        var enrolled = await db.StudentEnrolments.CountAsync(e => e.CampusId == key.CampusId && e.AcademicYearId == key.AcademicYearId &&
                                                                  e.YearGroupId == key.YearGroupId && e.Status == EnrolmentStatus.Active, ct);
        var apps = db.Applications.Where(a => a.CampusId == key.CampusId && a.AcademicYearId == key.AcademicYearId && a.YearGroupId == key.YearGroupId);
        var accepted = await apps.CountAsync(a => a.Status == ApplicationStatus.Accepted, ct);
        var offered = await apps.CountAsync(a => a.Status == ApplicationStatus.Offered, ct);
        var waitlisted = await db.WaitlistEntries.CountAsync(w => w.CampusId == key.CampusId && w.AcademicYearId == key.AcademicYearId &&
                                                                  w.YearGroupId == key.YearGroupId && w.IsActive, ct);
        yearGroupName ??= await db.YearGroups.Where(y => y.Id == key.YearGroupId).Select(y => y.Name).FirstAsync(ct);

        return new SeatSummary(key.CampusId, key.AcademicYearId, key.YearGroupId, yearGroupName, total.Value, enrolled, accepted, offered,
            Math.Max(0, total.Value - enrolled - accepted - offered), waitlisted);
    }

    /// <summary>
    /// Scores every tested application in the class: weighted test + interview, plus sibling / staff-ward bonuses,
    /// with the year-group age cut-off as an eligibility rule. Eligible applications become Shortlisted.
    /// </summary>
    public async Task<List<MeritEntry>> ComputeMeritAsync(ClassKey key, CancellationToken ct)
    {
        var testWeight = await settings.GetDecimalAsync(SettingKeys.TestWeight, 0.7m, key.CampusId, ct);
        var interviewWeight = await settings.GetDecimalAsync(SettingKeys.InterviewWeight, 0.3m, key.CampusId, ct);
        var siblingBonus = await settings.GetDecimalAsync(SettingKeys.SiblingBonus, 5m, key.CampusId, ct);
        var staffBonus = await settings.GetDecimalAsync(SettingKeys.StaffWardBonus, 5m, key.CampusId, ct);

        var yg = await db.YearGroups.AsNoTracking().FirstAsync(y => y.Id == key.YearGroupId, ct);
        var year = await db.AcademicYears.AsNoTracking().FirstAsync(y => y.Id == key.AcademicYearId, ct);
        var cutOff = new DateOnly(year.StartDate.Year, yg.AgeCutOffMonth, Math.Min(yg.AgeCutOffDay, DateTime.DaysInMonth(year.StartDate.Year, yg.AgeCutOffMonth)));

        var apps = await db.Applications
            .Where(a => a.CampusId == key.CampusId && a.AcademicYearId == key.AcademicYearId && a.YearGroupId == key.YearGroupId &&
                        (a.Status == ApplicationStatus.Tested || a.Status == ApplicationStatus.Shortlisted))
            .ToListAsync(ct);

        var entries = new List<(Application App, bool Eligible, List<string> Notes)>();
        foreach (var a in apps)
        {
            var notes = new List<string>();
            var eligible = true;

            var ageMonths = AgeInMonths(a.DateOfBirth, cutOff);
            if (yg.MinAgeMonths is { } min && ageMonths < min)
            {
                eligible = false;
                notes.Add($"Under age: {ageMonths / 12}y {ageMonths % 12}m on {cutOff:dd MMM yyyy} (min {min / 12}y {min % 12}m).");
            }
            if (yg.MaxAgeMonths is { } max && ageMonths > max)
            {
                eligible = false;
                notes.Add($"Over age: {ageMonths / 12}y {ageMonths % 12}m on {cutOff:dd MMM yyyy} (max {max / 12}y {max % 12}m).");
            }
            if (a.TestScore is null)
            {
                eligible = false;
                notes.Add("No test score.");
            }

            if (a.TestScore is { } test)
            {
                decimal score = a.InterviewScore is { } interview && testWeight + interviewWeight > 0
                    ? (test * testWeight + interview * interviewWeight) / (testWeight + interviewWeight)
                    : test;
                if (a.HasSiblingInSchool) { score += siblingBonus; notes.Add($"Sibling priority +{siblingBonus}."); }
                if (a.IsStaffWard) { score += staffBonus; notes.Add($"Staff-ward priority +{staffBonus}."); }
                a.MeritScore = Math.Round(score, 2);
            }

            entries.Add((a, eligible, notes));
        }

        var rank = 1;
        foreach (var e in entries.Where(e => e.Eligible).OrderByDescending(e => e.App.MeritScore).ThenBy(e => e.App.SubmittedAtUtc))
        {
            e.App.MeritRank = rank++;
            e.App.Status = ApplicationStatus.Shortlisted;
        }
        foreach (var e in entries.Where(e => !e.Eligible))
        {
            e.App.MeritRank = null;
            e.App.Status = ApplicationStatus.Tested;
        }
        await db.SaveChangesAsync(ct);

        return entries
            .OrderBy(e => e.App.MeritRank ?? int.MaxValue).ThenByDescending(e => e.App.MeritScore)
            .Select(e => new MeritEntry(e.App.Id, e.App.ApplicationNo, e.App.FullName, e.App.DateOfBirth, e.App.TestScore, e.App.InterviewScore,
                e.App.HasSiblingInSchool, e.App.IsStaffWard, e.App.MeritScore, e.App.MeritRank, e.Eligible, e.Notes, e.App.Status))
            .ToList();
    }

    /// <summary>Offers seats to the top of the merit list (up to available seats) and wait-lists the remaining shortlisted applicants.</summary>
    public async Task<(List<Offer> Offers, List<WaitlistEntry> Waitlisted)> OfferFromMeritListAsync(ClassKey key, int? maxOffers, OfferOptions options, CancellationToken ct)
    {
        var seat = await SeatAsync(key, null, ct);
        var count = Math.Min(maxOffers ?? seat.Available, seat.Available);

        var shortlisted = await db.Applications
            .Where(a => a.CampusId == key.CampusId && a.AcademicYearId == key.AcademicYearId && a.YearGroupId == key.YearGroupId &&
                        a.Status == ApplicationStatus.Shortlisted)
            .OrderBy(a => a.MeritRank).ToListAsync(ct);

        var offers = new List<Offer>();
        foreach (var app in shortlisted.Take(count))
            offers.Add(await CreateOfferAsync(app, options, ct));

        var waitlisted = new List<WaitlistEntry>();
        foreach (var app in shortlisted.Skip(count))
            waitlisted.Add(await AddToWaitlistAsync(app, ct));

        await db.SaveChangesAsync(ct);
        return (offers, waitlisted);
    }

    public async Task<Offer> CreateOfferAsync(Application app, OfferOptions options, CancellationToken ct)
    {
        if (app.Status is not (ApplicationStatus.Shortlisted or ApplicationStatus.Waitlisted or ApplicationStatus.Tested or ApplicationStatus.UnderReview or ApplicationStatus.Submitted))
            throw new DomainException($"Cannot offer a seat to an application in status {app.Status}.", "invalid_status", isConflict: true);
        if (await db.Offers.AnyAsync(o => o.ApplicationId == app.Id && o.Status == OfferStatus.Pending, ct))
            throw new DomainException("This application already has a pending offer.", "offer_exists", isConflict: true);

        var validity = options.ExpiresInDays ?? await settings.GetIntAsync(SettingKeys.OfferValidityDays, 7, app.CampusId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        var offer = new Offer
        {
            OfferNo = await numbers.NextAsync(NumberKeys.Offer, app.CampusId, app.AcademicYearId, ct),
            CampusId = app.CampusId,
            ApplicationId = app.Id,
            IssuedAtUtc = now,
            ExpiresAtUtc = now.AddDays(validity),
            FeeScheduleText = options.FeeScheduleText,
            FeeLinesJson = options.FeeLines is { Count: > 0 } ? JsonSerializer.Serialize(options.FeeLines) : null,
            Language = options.Language ?? "en",
            ResponseToken = token
        };
        db.Offers.Add(offer);
        app.Status = ApplicationStatus.Offered;

        var waiting = await db.WaitlistEntries.Where(w => w.ApplicationId == app.Id && w.IsActive).ToListAsync(ct);
        foreach (var w in waiting)
        {
            w.IsActive = false;
            w.PromotedAtUtc = now;
        }
        await SetEnquiryStageAsync(app, Domain.Admission.EnquiryStage.Offered, ct);

        var baseUrl = configuration["PublicSite:BaseUrl"] ?? "https://lighthouseschoolbd.com";
        await notifier.NotifyAsync(app, TemplateCodes.OfferIssued, new Dictionary<string, string?>
        {
            ["ExpiresOn"] = Infrastructure.Localization.BanglaFormat.ToDhaka(offer.ExpiresAtUtc).ToString("dd MMM yyyy"),
            ["ResponseLink"] = $"{baseUrl.TrimEnd('/')}/admission/offer/{token}",
            ["FeeSchedule"] = options.FeeScheduleText
        }, ct, alsoEmail: true);
        return offer;
    }

    public async Task<WaitlistEntry> AddToWaitlistAsync(Application app, CancellationToken ct)
    {
        var existing = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.ApplicationId == app.Id && w.IsActive, ct);
        if (existing is not null) return existing;

        var position = await db.WaitlistEntries
            .Where(w => w.CampusId == app.CampusId && w.AcademicYearId == app.AcademicYearId && w.YearGroupId == app.YearGroupId && w.IsActive)
            .MaxAsync(w => (int?)w.Position, ct) ?? 0;
        // Entries added in this unit of work are not in the database yet.
        position = Math.Max(position, db.WaitlistEntries.Local
            .Where(w => w.CampusId == app.CampusId && w.AcademicYearId == app.AcademicYearId && w.YearGroupId == app.YearGroupId && w.IsActive)
            .Select(w => w.Position).DefaultIfEmpty(0).Max());

        var entry = new WaitlistEntry
        {
            CampusId = app.CampusId, AcademicYearId = app.AcademicYearId, YearGroupId = app.YearGroupId,
            ApplicationId = app.Id, Position = position + 1
        };
        db.WaitlistEntries.Add(entry);
        app.Status = ApplicationStatus.Waitlisted;
        return entry;
    }

    /// <summary>
    /// Called whenever a seat may have been freed (offer declined/expired/revoked, student withdrawn):
    /// offers seats to the head of the waiting list while seats are available. Saves.
    /// </summary>
    public async Task<int> PromoteWaitlistAsync(ClassKey key, CancellationToken ct)
    {
        var promoted = 0;
        while (true)
        {
            var seat = await SeatAsync(key, null, ct);
            if (seat.Available <= 0) break;

            var next = await db.WaitlistEntries.Include(w => w.Application)
                .Where(w => w.CampusId == key.CampusId && w.AcademicYearId == key.AcademicYearId && w.YearGroupId == key.YearGroupId && w.IsActive)
                .OrderBy(w => w.Position).FirstOrDefaultAsync(ct);
            if (next is null) break;

            await notifier.NotifyAsync(next.Application!, TemplateCodes.WaitlistPromoted, [], ct);
            await CreateOfferAsync(next.Application!, await LatestTermsAsync(key, ct), ct);
            await db.SaveChangesAsync(ct);
            promoted++;
        }
        return promoted;
    }

    /// <summary>A promoted wait-list applicant gets the same fee terms as the class's most recent offer.</summary>
    private async Task<OfferOptions> LatestTermsAsync(ClassKey key, CancellationToken ct)
    {
        var latest = await db.Offers.AsNoTracking()
            .Where(o => o.CampusId == key.CampusId && o.Application!.AcademicYearId == key.AcademicYearId && o.Application.YearGroupId == key.YearGroupId)
            .OrderByDescending(o => o.IssuedAtUtc)
            .Select(o => new { o.FeeScheduleText, o.FeeLinesJson, o.Language })
            .FirstOrDefaultAsync(ct);
        var lines = string.IsNullOrEmpty(latest?.FeeLinesJson) ? null : JsonSerializer.Deserialize<List<FeeLine>>(latest.FeeLinesJson);
        return new OfferOptions(null, latest?.FeeScheduleText, lines, latest?.Language);
    }

    public async Task RespondAsync(Offer offer, bool accept, string? note, CancellationToken ct)
    {
        if (offer.Status != OfferStatus.Pending)
            throw new DomainException($"This offer is already {offer.Status}.", "offer_closed", isConflict: true);
        var now = clock.GetUtcNow().UtcDateTime;
        if (offer.ExpiresAtUtc < now)
            throw new DomainException("This offer has expired.", "offer_expired", isConflict: true);

        var app = offer.Application ?? await db.Applications.FirstAsync(a => a.Id == offer.ApplicationId, ct);
        offer.RespondedAtUtc = now;
        offer.ResponseNote = note;

        if (!accept)
        {
            offer.Status = OfferStatus.Declined;
            app.Status = ApplicationStatus.Declined;
            await SetEnquiryStageAsync(app, Domain.Admission.EnquiryStage.Lost, ct);
            await db.SaveChangesAsync(ct);
            await PromoteWaitlistAsync(new ClassKey(app.CampusId, app.AcademicYearId, app.YearGroupId), ct);
            return;
        }

        offer.Status = OfferStatus.Accepted;
        app.Status = ApplicationStatus.Accepted;
        await RaiseAdmissionInvoiceAsync(app, offer, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>ADM-07: the admission/session invoice raised on acceptance from the fee lines quoted in the offer.</summary>
    private async Task RaiseAdmissionInvoiceAsync(Application app, Offer offer, CancellationToken ct)
    {
        var lines = string.IsNullOrEmpty(offer.FeeLinesJson) ? [] : JsonSerializer.Deserialize<List<FeeLine>>(offer.FeeLinesJson) ?? [];
        if (lines.Count == 0) return;

        var invoice = new AdmissionInvoice
        {
            InvoiceNo = await numbers.NextAsync(NumberKeys.AdmissionInvoice, app.CampusId, app.AcademicYearId, ct),
            CampusId = app.CampusId,
            ApplicationId = app.Id,
            OfferId = offer.Id,
            DueDate = DateOnly.FromDateTime(Infrastructure.Localization.BanglaFormat.ToDhaka(clock.GetUtcNow().UtcDateTime)).AddDays(7),
            Amount = lines.Sum(l => l.Amount)
        };
        foreach (var l in lines)
            invoice.Lines.Add(new AdmissionInvoiceLine { InvoiceId = invoice.Id, FeeHeadId = l.FeeHeadId, Description = l.Description, Amount = l.Amount });
        db.AdmissionInvoices.Add(invoice);

        await notifier.NotifyAsync(app, TemplateCodes.AdmissionInvoiceIssued, new Dictionary<string, string?>
        {
            ["InvoiceNo"] = invoice.InvoiceNo,
            ["Amount"] = Infrastructure.Localization.BanglaFormat.GroupLakhCrore(invoice.Amount),
            ["DueDate"] = invoice.DueDate.ToString("dd MMM yyyy")
        }, ct);
    }

    public async Task SetEnquiryStageAsync(Application app, Domain.Admission.EnquiryStage stage, CancellationToken ct)
    {
        if (app.EnquiryId is null) return;
        var enquiry = await db.Enquiries.FirstOrDefaultAsync(e => e.Id == app.EnquiryId, ct);
        if (enquiry is not null && enquiry.Stage != Domain.Admission.EnquiryStage.Admitted)
            enquiry.Stage = stage;
    }

    internal static int AgeInMonths(DateOnly dob, DateOnly on)
    {
        var months = (on.Year - dob.Year) * 12 + on.Month - dob.Month;
        if (on.Day < dob.Day) months--;
        return months;
    }
}

/// <summary>ADM-06: expires unanswered offers and hands their seats to the waiting list.</summary>
public sealed class OfferExpiryJob : IScheduledJob
{
    public string Name => "offer-expiry";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var selection = services.GetRequiredService<SelectionService>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var expired = await db.Offers.Include(o => o.Application)
            .Where(o => o.Status == OfferStatus.Pending && o.ExpiresAtUtc < now).ToListAsync(ct);
        if (expired.Count == 0) return;

        var classes = new HashSet<ClassKey>();
        foreach (var offer in expired)
        {
            offer.Status = OfferStatus.Expired;
            var app = offer.Application!;
            app.Status = ApplicationStatus.Declined;
            app.DecisionNote = "Offer expired without a response.";
            classes.Add(new ClassKey(app.CampusId, app.AcademicYearId, app.YearGroupId));
        }
        await db.SaveChangesAsync(ct);

        foreach (var key in classes)
            await selection.PromoteWaitlistAsync(key, ct);
    }
}
