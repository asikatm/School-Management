namespace SchoolManagement.Api.Modules.Core;

/// <summary>
/// CORE-09: one search box for students, guardians, applicants and staff by name, ID, phone, birth-certificate
/// or NID number. Campus scoping is applied by the global query filters; each section also needs its view permission.
/// </summary>
[ApiController]
[Route("api/search")]
[Tags("Core: Search")]
public sealed class SearchController(AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    private const int PerType = 10;

    [HttpGet]
    [Authorize]
    public async Task<SearchResponse> Search([FromQuery] string q, CancellationToken ct)
    {
        q = (q ?? "").Trim();
        if (q.Length < 2) throw new DomainException("Type at least 2 characters.", "query_too_short");

        var results = new List<SearchHit>();

        if (currentUser.HasPermission(Permissions.Students.View))
        {
            var students = await db.Students.AsNoTracking()
                .Where(s => s.AdmissionNo == q || s.FirstName.Contains(q) || (s.LastName != null && s.LastName.Contains(q)) ||
                            (s.NameBn != null && s.NameBn.Contains(q)) || s.BirthCertificateNo == q || s.NationalId == q ||
                            s.RfidNo == q || s.Phone == q ||
                            s.Guardians.Any(g => g.Guardian!.Phone == q))
                .OrderBy(s => s.FirstName).Take(PerType)
                .Select(s => new SearchHit("student", s.Id, s.FirstName + " " + (s.LastName ?? ""), $"{s.AdmissionNo} · {s.Status}", s.CampusId))
                .ToListAsync(ct);
            results.AddRange(students);

            // Guardians are linked to campuses only through their children.
            var guardians = await db.Guardians.AsNoTracking()
                .Where(g => g.Name.Contains(q) || g.Phone == q || g.AltPhone == q || g.NationalId == q || g.Email == q)
                .Where(g => g.Students.Any(sg => db.Students.Any(s => s.Id == sg.StudentId)))
                .OrderBy(g => g.Name).Take(PerType)
                .Select(g => new SearchHit("guardian", g.Id, g.Name, g.Phone, null))
                .ToListAsync(ct);
            results.AddRange(guardians);
        }

        if (currentUser.HasPermission(Permissions.Admission.View))
        {
            var applications = await db.Applications.AsNoTracking()
                .Where(a => a.ApplicationNo == q || a.FirstName.Contains(q) || (a.LastName != null && a.LastName.Contains(q)) || a.BirthCertificateNo == q ||
                            a.Guardians.Any(g => g.Phone == q))
                .OrderByDescending(a => a.CreatedAtUtc).Take(PerType)
                .Select(a => new SearchHit("application", a.Id, a.FirstName + " " + (a.LastName ?? ""), $"{a.ApplicationNo} · {a.Status}", a.CampusId))
                .ToListAsync(ct);
            results.AddRange(applications);

            var enquiries = await db.Enquiries.AsNoTracking()
                .Where(e => e.EnquiryNo == q || e.ChildName.Contains(q) || e.GuardianName.Contains(q) || e.GuardianPhone == q)
                .OrderByDescending(e => e.CreatedAtUtc).Take(PerType)
                .Select(e => new SearchHit("enquiry", e.Id, e.ChildName, $"{e.EnquiryNo} · {e.Stage}", e.CampusId))
                .ToListAsync(ct);
            results.AddRange(enquiries);
        }

        if (currentUser.HasPermission(Permissions.Users.View))
        {
            var users = await db.Users.AsNoTracking()
                .Where(u => u.UserType == UserType.Staff && (u.FullName.Contains(q) || u.UserName == q || u.Phone == q || u.Email == q))
                .OrderBy(u => u.FullName).Take(PerType)
                .Select(u => new SearchHit("staff", u.Id, u.FullName, u.UserName, null))
                .ToListAsync(ct);
            results.AddRange(users);
        }

        return new SearchResponse(q, results);
    }
}

public record SearchHit(string Type, Guid Id, string Title, string? Subtitle, Guid? CampusId);
public record SearchResponse(string Query, IReadOnlyList<SearchHit> Results);
