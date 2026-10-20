using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Notifications;

namespace SchoolManagement.Api.Modules.Admission.Services;

/// <summary>Sends admission messages to an applicant's primary guardian by SMS (and e-mail when a template exists).</summary>
public sealed class AdmissionNotifier(AppDbContext db, INotificationService notifications)
{
    public async Task NotifyAsync(Application application, string templateCode, Dictionary<string, string?> data, CancellationToken ct, bool alsoEmail = false)
    {
        var guardian = application.Guardians.FirstOrDefault(g => g.IsPrimaryContact) ?? application.Guardians.FirstOrDefault();
        if (guardian is null)
        {
            guardian = await db.ApplicationGuardians.AsNoTracking()
                .Where(g => g.ApplicationId == application.Id).OrderByDescending(g => g.IsPrimaryContact).FirstOrDefaultAsync(ct);
            if (guardian is null) return;
        }

        var ctx = await ContextAsync(application, ct);
        foreach (var (k, v) in ctx) data.TryAdd(k, v);
        data.TryAdd("GuardianName", guardian.Name);
        data.TryAdd("ChildName", application.FullName);
        data.TryAdd("ApplicationNo", application.ApplicationNo);

        await notifications.QueueAsync(new NotificationRequest
        {
            TemplateCode = templateCode, Channel = NotificationChannel.Sms, Recipient = guardian.Phone, Data = data,
            Language = "bn", CampusId = application.CampusId, RelatedEntityType = nameof(Application), RelatedEntityId = application.Id.ToString()
        }, ct);

        if (alsoEmail && !string.IsNullOrWhiteSpace(guardian.Email))
            await notifications.QueueAsync(new NotificationRequest
            {
                TemplateCode = templateCode, Channel = NotificationChannel.Email, Recipient = guardian.Email, Data = data,
                Language = "en", CampusId = application.CampusId, RelatedEntityType = nameof(Application), RelatedEntityId = application.Id.ToString()
            }, ct);
    }

    public async Task<Dictionary<string, string?>> ContextAsync(Application application, CancellationToken ct)
    {
        var campus = await db.Campuses.AsNoTracking().Where(c => c.Id == application.CampusId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var cls = await db.YearGroups.AsNoTracking().Where(y => y.Id == application.YearGroupId).Select(y => y.Name).FirstOrDefaultAsync(ct);
        var year = await db.AcademicYears.AsNoTracking().Where(y => y.Id == application.AcademicYearId).Select(y => y.Name).FirstOrDefaultAsync(ct);
        var school = await db.Organisations.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct);
        return new Dictionary<string, string?>
        {
            ["CampusName"] = campus, ["ClassName"] = cls, ["AcademicYear"] = year, ["SchoolName"] = school
        };
    }
}
