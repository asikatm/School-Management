using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Numbering;

namespace SchoolManagement.Api.Modules.Students.Services;

public record ImportRowResult(int Row, string? Name, bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public record ImportResult(bool DryRun, int TotalRows, int ValidRows, int ImportedRows, IReadOnlyList<ImportRowResult> Rows);

public record StudentExportRow(string AdmissionNo, string FirstName, string? LastName, string? NameBn, DateOnly DateOfBirth, string? Gender, string? BloodGroup,
    string Campus, string? ClassName, string? Section, int? RollNo, StudentStatus Status, string? PrimaryGuardian, string? GuardianPhone, string? GuardianEmail);

/// <summary>CORE-10: Excel template, validation preview (dry run) and import for students with guardians; Excel/CSV export.</summary>
public sealed partial class StudentImportService(AppDbContext db, INumberSequenceService numbers, ICurrentUser currentUser)
{
    public static readonly string[] Columns =
    [
        "AdmissionNo", "FirstName*", "LastName", "NameBn", "DateOfBirth*", "Gender", "Religion", "BloodGroup", "BirthCertificateNo",
        "CampusCode*", "YearGroupCode*", "Section", "RollNo", "AdmissionDate", "PresentAddress",
        "Guardian1Name*", "Guardian1Relationship*", "Guardian1Phone*", "Guardian1Email",
        "Guardian2Name", "Guardian2Relationship", "Guardian2Phone", "Guardian2Email"
    ];

    public byte[] Template()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Students");
        for (var i = 0; i < Columns.Length; i++)
        {
            ws.Cell(1, i + 1).Value = Columns[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }
        string[] sample = ["", "Ayaan", "Rahman", "আয়ান রহমান", "2015-03-14", "Male", "Islam", "B+", "20151234567890123", "DHN", "Y5", "A", "", "2026-07-01",
            "House 8, Road 1, Shyamoli", "Karim Rahman", "Father", "01711000000", "karim@example.com", "Nasrin Akter", "Mother", "01811000000", ""];
        for (var i = 0; i < sample.Length; i++) ws.Cell(2, i + 1).Value = sample[i];
        ws.Columns().AdjustToContents();

        var help = wb.AddWorksheet("Help");
        help.Cell(1, 1).Value = "Columns marked * are required. Dates as YYYY-MM-DD. Phone as 01XXXXXXXXX or +8801XXXXXXXXX.";
        help.Cell(2, 1).Value = "Leave AdmissionNo blank to generate one. Guardians with the same phone are linked as one family (siblings).";
        help.Cell(3, 1).Value = "Run with dryRun=true first; nothing is saved until every row is valid and dryRun=false.";
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<ImportResult> ImportAsync(Stream xlsx, Guid academicYearId, bool dryRun, CancellationToken ct)
    {
        using var wb = new XLWorkbook(xlsx);
        var ws = wb.Worksheet(1);
        var header = ws.Row(1).CellsUsed().ToDictionary(c => c.GetString().Trim().TrimEnd('*'), c => c.Address.ColumnNumber, StringComparer.OrdinalIgnoreCase);
        var missing = Columns.Where(c => c.EndsWith('*') && !header.ContainsKey(c.TrimEnd('*'))).ToList();
        if (missing.Count > 0) throw new DomainException($"Missing required columns: {string.Join(", ", missing)}.", "invalid_template");

        var campuses = await db.Campuses.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.Id, StringComparer.OrdinalIgnoreCase, ct);
        var yearGroups = await db.YearGroups.AsNoTracking().ToDictionaryAsync(y => y.Code, y => y.Id, StringComparer.OrdinalIgnoreCase, ct);
        var sections = await db.Sections.AsNoTracking().Where(s => s.AcademicYearId == academicYearId)
            .Select(s => new { s.Id, s.CampusId, s.YearGroupId, s.Name }).ToListAsync(ct);
        var year = await db.AcademicYears.AsNoTracking().FirstOrDefaultAsync(y => y.Id == academicYearId, ct) ?? throw new NotFoundException("Academic year", academicYearId);

        var parsed = new List<(int Row, ParsedStudent? Data, ImportRowResult Result)>();
        var seenBirthCerts = new HashSet<string>();
        var seenNameDob = new HashSet<string>();
        var seenAdmissionNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            string Get(string col) => header.TryGetValue(col, out var idx) ? row.Cell(idx).GetFormattedString().Trim() : "";
            var errors = new List<string>();
            var warnings = new List<string>();

            var first = Get("FirstName");
            if (first.Length == 0) errors.Add("FirstName is required.");
            var dob = ParseDate(row, header, "DateOfBirth", errors, required: true);
            var admissionDate = ParseDate(row, header, "AdmissionDate", errors, required: false) ?? year.StartDate;

            Guid campusId = Guid.Empty, yearGroupId = Guid.Empty;
            Guid? sectionId = null;
            if (!campuses.TryGetValue(Get("CampusCode"), out campusId)) errors.Add($"Unknown campus code '{Get("CampusCode")}'.");
            else if (!currentUser.HasPermission(Permissions.Students.Create, campusId)) errors.Add("You cannot add students to this campus.");
            if (!yearGroups.TryGetValue(Get("YearGroupCode"), out yearGroupId)) errors.Add($"Unknown year group code '{Get("YearGroupCode")}'.");
            var sectionName = Get("Section");
            if (sectionName.Length > 0 && campusId != Guid.Empty && yearGroupId != Guid.Empty)
            {
                sectionId = sections.FirstOrDefault(s => s.CampusId == campusId && s.YearGroupId == yearGroupId && s.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase))?.Id;
                if (sectionId is null) errors.Add($"Section '{sectionName}' does not exist for this campus and class in {year.Name}.");
            }
            int? roll = null;
            if (Get("RollNo") is { Length: > 0 } rollText)
            {
                if (int.TryParse(rollText, out var r) && r > 0) roll = r;
                else errors.Add("RollNo must be a positive number.");
            }

            var guardians = new List<ParsedGuardian>();
            foreach (var n in new[] { 1, 2 })
            {
                var name = Get($"Guardian{n}Name");
                var rel = Get($"Guardian{n}Relationship");
                var phone = NormalisePhone(Get($"Guardian{n}Phone"));
                var email = Get($"Guardian{n}Email");
                if (name.Length == 0 && n == 2) continue;
                if (name.Length == 0) { errors.Add("Guardian1Name is required."); continue; }
                if (rel.Length == 0) errors.Add($"Guardian{n}Relationship is required.");
                if (phone is null) errors.Add($"Guardian{n}Phone is not a valid Bangladeshi mobile number.");
                if (email.Length > 0 && !email.Contains('@')) errors.Add($"Guardian{n}Email is not valid.");
                guardians.Add(new ParsedGuardian(name, rel, phone ?? "", email.Length > 0 ? email : null, IsPrimary: n == 1));
            }

            var admissionNo = Get("AdmissionNo");
            if (admissionNo.Length > 0)
            {
                if (!seenAdmissionNos.Add(admissionNo)) errors.Add($"AdmissionNo {admissionNo} appears twice in the file.");
                else if (await db.Students.IgnoreQueryFilters().AnyAsync(s => s.AdmissionNo == admissionNo, ct)) errors.Add($"AdmissionNo {admissionNo} already exists.");
            }

            var birthCert = Get("BirthCertificateNo");
            if (birthCert.Length > 0)
            {
                if (!seenBirthCerts.Add(birthCert)) errors.Add("Birth certificate number appears twice in the file.");
                else if (await db.Students.IgnoreQueryFilters().AnyAsync(s => s.BirthCertificateNo == birthCert && !s.IsDeleted, ct))
                    errors.Add("A student with this birth certificate number already exists.");
            }
            else warnings.Add("No birth certificate number.");

            if (dob is not null && first.Length > 0)
            {
                var last = NullIfEmpty(Get("LastName"));
                var key = $"{first}|{last}|{dob}".ToLowerInvariant();
                if (!seenNameDob.Add(key)) warnings.Add("Same name and date of birth as another row (possible duplicate).");
                else if (await db.Students.AnyAsync(s => s.FirstName == first && s.LastName == last && s.DateOfBirth == dob, ct))
                    warnings.Add("A student with the same name and date of birth already exists (possible duplicate).");
            }

            var result = new ImportRowResult(row.RowNumber(), $"{first} {Get("LastName")}".Trim(), errors.Count == 0, errors, warnings);
            var data = errors.Count == 0
                ? new ParsedStudent(admissionNo.Length > 0 ? admissionNo : null, first, NullIfEmpty(Get("LastName")), NullIfEmpty(Get("NameBn")), dob!.Value,
                    NullIfEmpty(Get("Gender")), NullIfEmpty(Get("Religion")), NullIfEmpty(Get("BloodGroup")), NullIfEmpty(birthCert),
                    campusId, yearGroupId, sectionId, roll, admissionDate, NullIfEmpty(Get("PresentAddress")), guardians)
                : null;
            parsed.Add((row.RowNumber(), data, result));
        }

        var validRows = parsed.Count(p => p.Result.Valid);
        if (dryRun || validRows != parsed.Count)
            return new ImportResult(true, parsed.Count, validRows, 0, parsed.Select(p => p.Result).ToList());

        foreach (var (_, data, _) in parsed)
            await CreateAsync(data!, academicYearId, ct);
        await db.SaveChangesAsync(ct);
        return new ImportResult(false, parsed.Count, validRows, validRows, parsed.Select(p => p.Result).ToList());
    }

    private async Task CreateAsync(ParsedStudent p, Guid academicYearId, CancellationToken ct)
    {
        var guardians = new List<(Guardian Guardian, ParsedGuardian Source)>();
        Guid? familyId = null;
        foreach (var g in p.Guardians)
        {
            var guardian = db.Guardians.Local.FirstOrDefault(x => x.Phone == g.Phone)
                           ?? await db.Guardians.FirstOrDefaultAsync(x => x.Phone == g.Phone, ct);
            if (guardian is null)
            {
                guardian = new Guardian { Name = g.Name, Phone = g.Phone, Email = g.Email };
                db.Guardians.Add(guardian);
            }
            familyId ??= guardian.FamilyId;
            guardians.Add((guardian, g));
        }

        if (familyId is null)
        {
            var family = new Family { FamilyCode = await numbers.NextAsync(NumberKeys.Family, ct: ct), Name = p.Guardians[0].Name };
            db.Families.Add(family);
            familyId = family.Id;
        }
        foreach (var (g, _) in guardians) g.FamilyId ??= familyId;

        var student = new Student
        {
            AdmissionNo = p.AdmissionNo ?? await numbers.NextAsync(NumberKeys.AdmissionNo, p.CampusId, academicYearId, ct),
            CampusId = p.CampusId, FamilyId = familyId, FirstName = p.FirstName, LastName = p.LastName, NameBn = p.NameBn,
            DateOfBirth = p.DateOfBirth, Gender = p.Gender, Religion = p.Religion, BloodGroup = p.BloodGroup,
            BirthCertificateNo = p.BirthCertificateNo, PresentAddress = p.PresentAddress, AdmissionDate = p.AdmissionDate
        };
        db.Students.Add(student);
        var enrolment = new StudentEnrolment
        {
            StudentId = student.Id, CampusId = p.CampusId, AcademicYearId = academicYearId, YearGroupId = p.YearGroupId,
            SectionId = p.SectionId, RollNo = p.RollNo, StartDate = p.AdmissionDate
        };
        db.StudentEnrolments.Add(enrolment);
        student.CurrentEnrolmentId = enrolment.Id;
        foreach (var (g, src) in guardians)
            db.StudentGuardians.Add(new StudentGuardian { StudentId = student.Id, GuardianId = g.Id, Relationship = src.Relationship, IsPrimaryContact = src.IsPrimary, IsEmergencyContact = src.IsPrimary });
    }

    public byte[] ExportXlsx(IReadOnlyList<StudentExportRow> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Students");
        ws.Cell(1, 1).InsertTable(rows, "Students", true);
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    public static byte[] ExportCsv(IReadOnlyList<StudentExportRow> rows)
    {
        var sb = new StringBuilder("AdmissionNo,FirstName,LastName,NameBn,DateOfBirth,Gender,BloodGroup,Campus,Class,Section,RollNo,Status,PrimaryGuardian,GuardianPhone,GuardianEmail\n");
        foreach (var r in rows)
            sb.AppendJoin(',', Csv(r.AdmissionNo), Csv(r.FirstName), Csv(r.LastName), Csv(r.NameBn), r.DateOfBirth.ToString("yyyy-MM-dd"), Csv(r.Gender),
                Csv(r.BloodGroup), Csv(r.Campus), Csv(r.ClassName), Csv(r.Section), r.RollNo, r.Status, Csv(r.PrimaryGuardian), Csv(r.GuardianPhone), Csv(r.GuardianEmail)).Append('\n');
        // UTF-8 BOM so Excel shows Bangla names correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Csv(string? v) => v is null ? "" : "\"" + v.Replace("\"", "\"\"") + "\"";

    private static DateOnly? ParseDate(IXLRow row, Dictionary<string, int> header, string col, List<string> errors, bool required)
    {
        if (!header.TryGetValue(col, out var idx)) { if (required) errors.Add($"{col} is required."); return null; }
        var cell = row.Cell(idx);
        if (cell.IsEmpty()) { if (required) errors.Add($"{col} is required."); return null; }
        if (cell.DataType == XLDataType.DateTime) return DateOnly.FromDateTime(cell.GetDateTime());
        var text = cell.GetFormattedString().Trim();
        if (DateOnly.TryParseExact(text, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        errors.Add($"{col} '{text}' is not a valid date (use YYYY-MM-DD).");
        return null;
    }

    /// <summary>Accepts 01XXXXXXXXX, 8801XXXXXXXXX or +8801XXXXXXXXX and returns +8801XXXXXXXXX.</summary>
    public static string? NormalisePhone(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = NonDigits().Replace(input, "");
        if (digits.StartsWith("880")) digits = digits[2..];
        return BdMobile().IsMatch(digits) ? "+88" + digits : null;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();

    [GeneratedRegex(@"^01[3-9]\d{8}$")]
    private static partial Regex BdMobile();

    private record ParsedGuardian(string Name, string Relationship, string Phone, string? Email, bool IsPrimary);

    private record ParsedStudent(string? AdmissionNo, string FirstName, string? LastName, string? NameBn, DateOnly DateOfBirth, string? Gender, string? Religion,
        string? BloodGroup, string? BirthCertificateNo, Guid CampusId, Guid YearGroupId, Guid? SectionId, int? RollNo, DateOnly AdmissionDate,
        string? PresentAddress, List<ParsedGuardian> Guardians);
}
