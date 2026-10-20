using System.Net;
using System.Text;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Documents;
using SchoolManagement.Infrastructure.Numbering;
using SchoolManagement.Infrastructure.Settings;

namespace SchoolManagement.Api.Modules.Students.Services;

public record IdCardData(Guid StudentId, string CardNo, string AdmissionNo, string Name, string? NameBn, string ClassName, string? Section, int? RollNo,
    string? BloodGroup, string CampusName, DateOnly ValidUntil, string? RfidNo, string QrPayload, bool HasPhoto);

/// <summary>SIS-08: ID cards with QR/RFID, batch issue for a section and paid reissue of lost cards.</summary>
public sealed class IdCardService(
    AppDbContext db,
    INumberSequenceService numbers,
    ISettingsService settings,
    IQrCodeService qr,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public async Task<List<IdCardData>> IssueAsync(IReadOnlyCollection<Guid> studentIds, DateOnly validUntil, CancellationToken ct)
    {
        var students = await db.Students.Where(s => studentIds.Contains(s.Id) && s.Status == StudentStatus.Active).ToListAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var s in students)
        {
            currentUser.EnsurePermission(Permissions.Students.Edit, s.CampusId);
            await db.IdCards.Where(c => c.StudentId == s.Id && c.IsActive).ForEachAsync(c => c.IsActive = false, ct);
            db.IdCards.Add(new IdCard { StudentId = s.Id, CardNo = await numbers.NextAsync(NumberKeys.IdCard, ct: ct), IssuedAtUtc = now, ValidUntil = validUntil });
        }
        await db.SaveChangesAsync(ct);
        return await CardDataAsync(students.Select(s => s.Id).ToList(), ct);
    }

    /// <summary>Lost-card reissue: deactivates the old card and records the configured fee (billed by FEE in release 2).</summary>
    public async Task<IdCardData> ReissueAsync(Guid studentId, string reason, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct) ?? throw new NotFoundException("Student", studentId);
        currentUser.EnsurePermission(Permissions.Students.Edit, student.CampusId);
        var previous = await db.IdCards.Where(c => c.StudentId == studentId && c.IsActive).OrderByDescending(c => c.IssuedAtUtc).FirstOrDefaultAsync(ct)
                       ?? throw new DomainException("The student has no active card to reissue; issue a new card instead.", "no_card");

        previous.IsActive = false;
        db.IdCards.Add(new IdCard
        {
            StudentId = studentId,
            CardNo = await numbers.NextAsync(NumberKeys.IdCard, ct: ct),
            IssuedAtUtc = clock.GetUtcNow().UtcDateTime,
            ValidUntil = previous.ValidUntil,
            ReissueReason = reason,
            ReissueFee = await settings.GetDecimalAsync(SettingKeys.IdCardReissueFee, 0, student.CampusId, ct)
        });
        await db.SaveChangesAsync(ct);
        return (await CardDataAsync([studentId], ct)).Single();
    }

    public async Task<List<IdCardData>> CardDataAsync(List<Guid> studentIds, CancellationToken ct)
    {
        var rows = await (
            from s in db.Students.AsNoTracking()
            where studentIds.Contains(s.Id)
            join c in db.IdCards.AsNoTracking().Where(c => c.IsActive) on s.Id equals c.StudentId
            join e in db.StudentEnrolments.AsNoTracking() on s.CurrentEnrolmentId equals e.Id into es
            from e in es.DefaultIfEmpty()
            join y in db.YearGroups.AsNoTracking() on e.YearGroupId equals y.Id into ys
            from y in ys.DefaultIfEmpty()
            join sec in db.Sections.AsNoTracking() on e.SectionId equals sec.Id into secs
            from sec in secs.DefaultIfEmpty()
            join cp in db.Campuses.AsNoTracking() on s.CampusId equals cp.Id
            orderby sec.Name, e.RollNo, s.FirstName
            select new { s, c, ClassName = y != null ? y.Name : "-", Section = sec != null ? sec.Name : null, RollNo = e != null ? e.RollNo : null, Campus = cp.Name }
        ).ToListAsync(ct);

        return rows.Select(r => new IdCardData(r.s.Id, r.c.CardNo, r.s.AdmissionNo, r.s.FullName, r.s.NameBn, r.ClassName, r.Section, r.RollNo,
            r.s.BloodGroup, r.Campus, r.c.ValidUntil, r.s.RfidNo, QrPayload(r.c.CardNo, r.s.AdmissionNo), r.s.PhotoPath is not null)).ToList();
    }

    /// <summary>The QR carries only identifiers, never personal data, so a lost card leaks nothing.</summary>
    public static string QrPayload(string cardNo, string admissionNo) => $"SMS-ID:{cardNo}:{admissionNo}";

    /// <summary>A printable sheet of CR80-sized cards with inline QR codes.</summary>
    public string RenderHtml(IEnumerable<IdCardData> cards, string schoolName)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html><head><meta charset="utf-8"><title>ID cards</title><style>
            body{font-family:'Segoe UI','Noto Sans Bengali',sans-serif;margin:8mm}
            .card{display:inline-block;width:85.6mm;height:54mm;border:1px solid #1e3a5f;border-radius:3mm;margin:2mm;padding:3mm;box-sizing:border-box;vertical-align:top;position:relative;overflow:hidden}
            .card h1{font-size:9pt;margin:0 0 2mm;color:#1e3a5f}.card .n{font-weight:600;font-size:10pt}.card .bn{font-size:9pt}
            .card table{font-size:7.5pt;border-collapse:collapse}.card td{padding:0 2mm 0 0}
            .card img.qr{position:absolute;right:3mm;bottom:3mm;width:20mm;height:20mm}
            @media print{.card{break-inside:avoid}}
            </style></head><body>
            """);
        foreach (var c in cards)
        {
            var png = Convert.ToBase64String(qr.Png(c.QrPayload, 4));
            sb.Append($"""
                <div class="card"><h1>{E(schoolName)} — {E(c.CampusName)}</h1>
                <div class="n">{E(c.Name)}</div>{(c.NameBn is null ? "" : $"<div class=\"bn\">{E(c.NameBn)}</div>")}
                <table><tr><td>ID</td><td>{E(c.AdmissionNo)}</td></tr><tr><td>Class</td><td>{E(c.ClassName)} {E(c.Section)}</td></tr>
                <tr><td>Roll</td><td>{c.RollNo}</td></tr><tr><td>Blood</td><td>{E(c.BloodGroup)}</td></tr>
                <tr><td>Card</td><td>{E(c.CardNo)}</td></tr><tr><td>Valid to</td><td>{c.ValidUntil:dd MMM yyyy}</td></tr></table>
                <img class="qr" alt="QR" src="data:image/png;base64,{png}"/></div>
                """);
        }
        sb.Append("</body></html>");
        return sb.ToString();

        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    }
}
