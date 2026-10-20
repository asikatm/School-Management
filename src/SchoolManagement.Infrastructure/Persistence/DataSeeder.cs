using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolManagement.Domain.Approvals;
using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Identity;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Numbering;
using SchoolManagement.Infrastructure.Security;

namespace SchoolManagement.Infrastructure.Persistence;

/// <summary>
/// Seeds the reference configuration from the specification: Lighthouse International School with three campuses,
/// Pearson Edexcel programme structure, Appendix A grading schemes, CORE-03 roles with the spec 5.6 defaults.
/// Everything seeded here is ordinary data the school can edit afterwards.
/// </summary>
public sealed class DataSeeder(AppDbContext db, IPasswordHasher hasher, IConfiguration configuration, ILogger<DataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        using var _ = db.ElevateToSystem();
        await SeedRolesAsync(ct);
        if (await db.Organisations.IgnoreQueryFilters().AnyAsync(ct))
            return;

        logger.LogInformation("Seeding reference data");
        var org = new Organisation { Name = "Lighthouse International School", NameBn = "লাইটহাউস ইন্টারন্যাশনাল স্কুল", Code = "LIS", Website = "https://lighthouseschoolbd.com" };
        db.Organisations.Add(org);

        Campus Campus(string code, string name, string nameBn) =>
            new() { OrganisationId = org.Id, Code = code, Name = name, NameBn = nameBn };
        var campuses = new[]
        {
            Campus("DHN", "Dhanmondi (Main Campus)", "ধানমন্ডি (প্রধান ক্যাম্পাস)"),
            Campus("GUL", "Gulshan", "গুলশান"),
            Campus("LAL", "Lalmatia", "লালমাটিয়া")
        };
        db.Campuses.AddRange(campuses);

        var year = new AcademicYear
        {
            OrganisationId = org.Id,
            Name = "2026-2027",
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2027, 6, 30),
            Status = AcademicYearStatus.Open,
            IsCurrent = true
        };
        year.Terms.Add(new Term { Name = "Term 1", NameBn = "প্রথম টার্ম", Sequence = 1, StartDate = new DateOnly(2026, 7, 1), EndDate = new DateOnly(2026, 12, 31) });
        year.Terms.Add(new Term { Name = "Term 2", NameBn = "দ্বিতীয় টার্ম", Sequence = 2, StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 6, 30) });
        db.AcademicYears.Add(year);

        var schemes = SeedGradingSchemes(org.Id);
        var yearGroups = SeedProgrammes(org.Id, schemes);

        foreach (var campus in campuses)
        {
            var morning = new Shift { CampusId = campus.Id, Name = "Morning", StartTime = new TimeOnly(7, 30), EndTime = new TimeOnly(13, 30) };
            db.Shifts.Add(morning);
            foreach (var yg in yearGroups)
                foreach (var name in new[] { "A", "B" })
                    db.Sections.Add(new Section { CampusId = campus.Id, AcademicYearId = year.Id, YearGroupId = yg.Id, ShiftId = morning.Id, Name = name, Capacity = 25 });
        }

        SeedSubjects(org.Id);
        foreach (var (name, bn, colour) in new[] { ("Ibn Sina", "ইবনে সিনা", "#1E88E5"), ("Al-Biruni", "আল-বিরুনী", "#43A047"), ("Ibn Khaldun", "ইবনে খালদুন", "#E53935"), ("Al-Khwarizmi", "আল-খোয়ারিজমি", "#FDD835") })
            db.Houses.Add(new House { OrganisationId = org.Id, Name = name, NameBn = bn, Colour = colour });

        foreach (var (code, name, bn, freq, refundable) in new[]
                 {
                     ("APPLICATION", "Application Fee", "আবেদন ফি", FeeFrequency.OneTime, false),
                     ("ADMISSION", "Admission Fee", "ভর্তি ফি", FeeFrequency.OneTime, false),
                     ("SESSION", "Session Fee", "সেশন ফি", FeeFrequency.Annual, false),
                     ("TUITION", "Tuition Fee", "টিউশন ফি", FeeFrequency.Monthly, false),
                     ("EXAM", "Examination Fee", "পরীক্ষা ফি", FeeFrequency.Termly, false),
                     ("LAB", "Lab Fee", "ল্যাব ফি", FeeFrequency.Annual, false),
                     ("TRANSPORT", "Transport Fee", "পরিবহন ফি", FeeFrequency.Monthly, false),
                     ("ACTIVITY", "Activity Fee", "কার্যক্রম ফি", FeeFrequency.Annual, false),
                     ("UNIFORM", "Uniform", "ইউনিফর্ম", FeeFrequency.OneTime, false),
                     ("EDEXCEL", "Edexcel Entry Fee", "এডেক্সেল এন্ট্রি ফি", FeeFrequency.OneTime, false),
                     ("CAUTION", "Caution Money", "জামানত", FeeFrequency.OneTime, true),
                     ("IDCARD", "ID Card Reissue", "আইডি কার্ড পুনঃইস্যু", FeeFrequency.OneTime, false)
                 })
            db.FeeHeads.Add(new FeeHead { OrganisationId = org.Id, Code = code, Name = name, NameBn = bn, Frequency = freq, IsRefundable = refundable });

        foreach (var (key, prefix, pattern) in new[]
                 {
                     (NumberKeys.Enquiry, "ENQ", "{PREFIX}{YY}{SEQ:5}"),
                     (NumberKeys.Application, "APP", "{PREFIX}{YY}{SEQ:5}"),
                     (NumberKeys.AdmitCard, "AC", "{PREFIX}-{CAMPUS}-{SEQ:5}"),
                     (NumberKeys.Offer, "OFR", "{PREFIX}{YY}{SEQ:5}"),
                     (NumberKeys.AdmissionInvoice, "AINV", "{PREFIX}{YY}{SEQ:6}"),
                     (NumberKeys.AdmissionNo, "", "{CAMPUS}{YY}{SEQ:4}"),
                     (NumberKeys.Family, "FAM", "{PREFIX}{SEQ:6}"),
                     (NumberKeys.IdCard, "IDC", "{PREFIX}{YY}{SEQ:6}"),
                     (NumberKeys.TransferCertificate, "TC", "{PREFIX}-{CAMPUS}-{YY}{SEQ:4}")
                 })
            db.NumberSequences.Add(new NumberSequence { Key = key, Prefix = prefix, Pattern = pattern });

        foreach (var (key, value, description) in new[]
                 {
                     (SettingKeys.RequirePaymentBeforeEnrolment, "true", "ADM-07: enrol only after the admission invoice is paid"),
                     (SettingKeys.OfferValidityDays, "7", "ADM-06: days before an unanswered offer expires"),
                     (SettingKeys.TestWeight, "0.7", "ADM-05: weight of the admission test score in the merit score"),
                     (SettingKeys.InterviewWeight, "0.3", "ADM-05: weight of the interview score in the merit score"),
                     (SettingKeys.SiblingBonus, "5", "ADM-05: merit points added for a sibling in the school"),
                     (SettingKeys.StaffWardBonus, "5", "ADM-05: merit points added for a staff ward"),
                     (SettingKeys.IdCardReissueFee, "200", "SIS-08: fee for a lost-card reissue (BDT)"),
                     (SettingKeys.SmsSenderId, "LIGHTHOUSE", "COM-02: masked SMS sender ID"),
                     (SettingKeys.SchoolPortalUrl, "https://portal.lighthouseschoolbd.com", "Link sent to guardians")
                 })
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = description });

        await db.SaveChangesAsync(ct);

        await SeedAdminAsync(ct);
        await SeedWorkflowsAsync(ct);
        SeedTemplates();
        await db.SaveChangesAsync(ct);
    }

    private Dictionary<string, GradingScheme> SeedGradingSchemes(Guid orgId)
    {
        GradingScheme Scheme(string code, string name, GradingScaleType type, params (string Grade, decimal Min, decimal Max, decimal? Gp, bool Pass, string? Desc, string? DescBn)[] bands)
        {
            var s = new GradingScheme { OrganisationId = orgId, Code = code, Name = name, ScaleType = type };
            foreach (var b in bands)
                s.Bands.Add(new GradeBand { Grade = b.Grade, MinPercent = b.Min, MaxPercent = b.Max, GradePoint = b.Gp, IsPass = b.Pass, Descriptor = b.Desc, DescriptorBn = b.DescBn });
            db.GradingSchemes.Add(s);
            return s;
        }

        return new Dictionary<string, GradingScheme>
        {
            ["PRIMARY"] = Scheme("PRIMARY", "Primary descriptors", GradingScaleType.Descriptor,
                ("EX", 85, 100, null, true, "Exceeding", "প্রত্যাশার ঊর্ধ্বে"),
                ("ME", 60, 84.99m, null, true, "Meeting", "প্রত্যাশা পূরণ"),
                ("WT", 0, 59.99m, null, true, "Working Towards", "অগ্রসরমান")),
            ["LETTER"] = Scheme("LETTER", "Lower Secondary letter grades", GradingScaleType.Letter,
                ("A", 80, 100, null, true, null, null), ("B", 70, 79.99m, null, true, null, null), ("C", 60, 69.99m, null, true, null, null),
                ("D", 50, 59.99m, null, true, null, null), ("E", 40, 49.99m, null, true, null, null), ("U", 0, 39.99m, null, false, "Ungraded", "অশ্রেণীকৃত")),
            ["IGCSE91"] = Scheme("IGCSE91", "International GCSE 9–1", GradingScaleType.Numeric9To1,
                ("9", 90, 100, null, true, null, null), ("8", 80, 89.99m, null, true, null, null), ("7", 70, 79.99m, null, true, null, null),
                ("6", 60, 69.99m, null, true, null, null), ("5", 50, 59.99m, null, true, null, null), ("4", 40, 49.99m, null, true, null, null),
                ("3", 30, 39.99m, null, true, null, null), ("2", 20, 29.99m, null, true, null, null), ("1", 10, 19.99m, null, true, null, null),
                ("U", 0, 9.99m, null, false, "Ungraded", "অশ্রেণীকৃত")),
            ["IAL"] = Scheme("IAL", "International A Level A*–E", GradingScaleType.LetterAStarToE,
                ("A*", 90, 100, null, true, null, null), ("A", 80, 89.99m, null, true, null, null), ("B", 70, 79.99m, null, true, null, null),
                ("C", 60, 69.99m, null, true, null, null), ("D", 50, 59.99m, null, true, null, null), ("E", 40, 49.99m, null, true, null, null),
                ("U", 0, 39.99m, null, false, "Ungraded", "অশ্রেণীকৃত")),
            ["GPA5"] = Scheme("GPA5", "Local GPA 5.00", GradingScaleType.Gpa5,
                ("A+", 80, 100, 5.00m, true, null, null), ("A", 70, 79.99m, 4.00m, true, null, null), ("A-", 60, 69.99m, 3.50m, true, null, null),
                ("B", 50, 59.99m, 3.00m, true, null, null), ("C", 40, 49.99m, 2.00m, true, null, null), ("D", 33, 39.99m, 1.00m, true, null, null),
                ("F", 0, 32.99m, 0.00m, false, null, null))
        };
    }

    private List<YearGroup> SeedProgrammes(Guid orgId, Dictionary<string, GradingScheme> schemes)
    {
        var all = new List<YearGroup>();
        var seq = 0;

        void Programme(string code, string name, string nameBn, string scheme, params (string Code, string Name, string NameBn)[] groups)
        {
            var p = new Programme { OrganisationId = orgId, Code = code, Name = name, NameBn = nameBn, Sequence = all.Count, DefaultGradingSchemeId = schemes[scheme].Id };
            db.Programmes.Add(p);
            foreach (var g in groups)
            {
                var minAge = 36 + seq * 12;
                var yg = new YearGroup { ProgrammeId = p.Id, Code = g.Code, Name = g.Name, NameBn = g.NameBn, Sequence = seq++, MinAgeMonths = minAge, MaxAgeMonths = minAge + 23, AgeCutOffMonth = 7, AgeCutOffDay = 1 };
                p.YearGroups.Add(yg);
                all.Add(yg);
            }
        }

        Programme("EY", "Early Years", "প্রারম্ভিক শিক্ষা", "PRIMARY", ("PG", "Playgroup", "প্লে-গ্রুপ"), ("NUR", "Nursery", "নার্সারি"), ("KG", "Kindergarten", "কেজি"));
        Programme("PRI", "Primary", "প্রাথমিক", "PRIMARY", Enumerable.Range(1, 6).Select(i => ($"Y{i}", $"Year {i}", $"ইয়ার {i}")).ToArray());
        Programme("LSEC", "Lower Secondary", "নিম্ন মাধ্যমিক", "LETTER", Enumerable.Range(7, 3).Select(i => ($"Y{i}", $"Year {i}", $"ইয়ার {i}")).ToArray());
        Programme("IGCSE", "Pearson Edexcel International GCSE", "আইজিসিএসই", "IGCSE91", ("Y10", "Year 10", "ইয়ার ১০"), ("Y11", "Year 11", "ইয়ার ১১"));
        Programme("IAL", "Pearson Edexcel International A Level", "আইএএল", "IAL", ("AS", "AS Level (Year 12)", "এএস লেভেল"), ("A2", "A2 Level (Year 13)", "এ২ লেভেল"));

        for (var i = 0; i < all.Count - 1; i++)
            all[i].NextYearGroupId = all[i + 1].Id;
        all[^1].IsFinalYear = true;
        return all;
    }

    private void SeedSubjects(Guid orgId)
    {
        foreach (var (code, name, bn, type) in new[]
                 {
                     ("ENG", "English", "ইংরেজি", SubjectType.Compulsory), ("MATH", "Mathematics", "গণিত", SubjectType.Compulsory),
                     ("SCI", "Science", "বিজ্ঞান", SubjectType.Compulsory), ("BAN", "Bangla", "বাংলা", SubjectType.Compulsory),
                     ("ISL", "Islamic Studies", "ইসলামিক স্টাডিজ", SubjectType.Compulsory), ("QUR", "Quran", "কুরআন", SubjectType.Compulsory),
                     ("ICT", "ICT", "আইসিটি", SubjectType.Compulsory), ("BGS", "Bangladesh & Global Studies", "বাংলাদেশ ও বিশ্বপরিচয়", SubjectType.Compulsory),
                     ("ARB", "Arabic", "আরবি", SubjectType.Optional), ("PHY", "Physics", "পদার্থবিজ্ঞান", SubjectType.Elective),
                     ("CHEM", "Chemistry", "রসায়ন", SubjectType.Elective), ("BIO", "Biology", "জীববিজ্ঞান", SubjectType.Elective),
                     ("ECO", "Economics", "অর্থনীতি", SubjectType.Elective), ("BUS", "Business", "ব্যবসায়", SubjectType.Elective),
                     ("ACC", "Accounting", "হিসাববিজ্ঞান", SubjectType.Elective), ("CS", "Computer Science", "কম্পিউটার বিজ্ঞান", SubjectType.Elective)
                 })
            db.Subjects.Add(new Subject { OrganisationId = orgId, Code = code, Name = name, NameBn = bn, Type = type });
    }

    /// <summary>Default role → permission map from spec 5.6. IT Admin always receives every permission, including new ones.</summary>
    private async Task SeedRolesAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, string[]>
        {
            [RoleNames.ItAdmin] = [.. Permissions.All],
            [RoleNames.Principal] = [.. Permissions.All.Where(p => p != Permissions.Backup.Manage)],
            [RoleNames.VicePrincipal] =
            [
                Permissions.Settings.View, Permissions.Audit.View, Permissions.Approvals.View,
                Permissions.Notifications.View, Permissions.Notifications.Send,
                Permissions.Admission.View, Permissions.Admission.Approve, Permissions.Admission.Export,
                Permissions.Students.View, Permissions.Students.Create, Permissions.Students.Edit, Permissions.Students.Approve,
                Permissions.Students.Export, Permissions.Students.Documents
            ],
            [RoleNames.HeadOfSection] =
            [
                Permissions.Settings.View, Permissions.Approvals.View, Permissions.Notifications.View, Permissions.Notifications.Send,
                Permissions.Admission.View, Permissions.Students.View, Permissions.Students.Edit, Permissions.Students.Export
            ],
            [RoleNames.ClassTeacher] = [Permissions.Settings.View, Permissions.Students.View, Permissions.Notifications.View, Permissions.Approvals.View],
            [RoleNames.SubjectTeacher] = [Permissions.Settings.View, Permissions.Students.View, Permissions.Notifications.View],
            [RoleNames.Accountant] =
            [
                Permissions.Settings.View, Permissions.Admission.View, Permissions.Students.View,
                Permissions.Notifications.View, Permissions.Notifications.Send, Permissions.Approvals.View
            ],
            [RoleNames.AdmissionOfficer] =
            [
                Permissions.Settings.View, Permissions.Admission.View, Permissions.Admission.Create, Permissions.Admission.Edit,
                Permissions.Admission.Approve, Permissions.Admission.Export, Permissions.Students.View, Permissions.Students.Create,
                Permissions.Notifications.View, Permissions.Notifications.Send
            ],
            [RoleNames.Librarian] = [Permissions.Students.View],
            [RoleNames.TransportInCharge] = [Permissions.Students.View],
            [RoleNames.Parent] = [],
            [RoleNames.Student] = []
        };

        var roles = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions).ToListAsync(ct);
        foreach (var (name, permissions) in map)
        {
            var role = roles.FirstOrDefault(r => r.Name == name);
            if (role is null)
            {
                role = new Role { Name = name, IsSystem = true, Description = $"Default {name} role" };
                foreach (var p in permissions) role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
                db.Roles.Add(role);
            }
            else if (name == RoleNames.ItAdmin)
            {
                foreach (var p in permissions.Where(p => role.Permissions.All(rp => rp.Permission != p)))
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedAdminAsync(CancellationToken ct)
    {
        var password = configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:AdminPassword is not set; no administrator account was created.");
            return;
        }

        var itAdmin = await db.Roles.FirstAsync(r => r.Name == RoleNames.ItAdmin, ct);
        var admin = new User
        {
            UserName = "admin",
            FullName = "System Administrator",
            Email = configuration["Seed:AdminEmail"],
            PasswordHash = hasher.Hash(password),
            UserType = UserType.Staff,
            MustChangePassword = true
        };
        admin.RoleAssignments.Add(new UserRoleAssignment { UserId = admin.Id, RoleId = itAdmin.Id, CampusId = null });
        db.Users.Add(admin);
        logger.LogWarning("Created administrator 'admin' with the configured seed password; change it after first login.");
    }

    private async Task SeedWorkflowsAsync(CancellationToken ct)
    {
        var roles = await db.Roles.ToDictionaryAsync(r => r.Name, r => r.Id, ct);

        ApprovalWorkflow Workflow(string type, string name, params (string Step, string Role, int? EscalateHours, string? EscalateRole)[] steps)
        {
            var wf = new ApprovalWorkflow { RequestType = type, Name = name };
            var order = 1;
            foreach (var s in steps)
                wf.Steps.Add(new ApprovalStep
                {
                    WorkflowId = wf.Id, Order = order++, Name = s.Step, ApproverRoleId = roles[s.Role],
                    EscalateAfterHours = s.EscalateHours, EscalateToRoleId = s.EscalateRole is null ? null : roles[s.EscalateRole]
                });
            db.ApprovalWorkflows.Add(wf);
            return wf;
        }

        Workflow(ApprovalTypes.Withdrawal, "Student withdrawal",
            ("Head of Section review", RoleNames.HeadOfSection, 48, RoleNames.Principal),
            ("Principal approval", RoleNames.Principal, null, null));
        Workflow(ApprovalTypes.FeeWaiver, "Fee waiver",
            ("Accounts review", RoleNames.Accountant, 24, RoleNames.Principal),
            ("Principal approval", RoleNames.Principal, null, null));
        Workflow(ApprovalTypes.Discount, "Discount / concession",
            ("Accounts review", RoleNames.Accountant, 24, RoleNames.Principal),
            ("Principal approval", RoleNames.Principal, null, null));
        Workflow(ApprovalTypes.CertificateIssue, "Certificate issue",
            ("Principal approval", RoleNames.Principal, 48, RoleNames.VicePrincipal));
    }

    private void SeedTemplates()
    {
        void T(string code, NotificationChannel channel, string lang, string? subject, string body) =>
            db.MessageTemplates.Add(new MessageTemplate { Code = code, Channel = channel, Language = lang, Subject = subject, Body = body });

        T(TemplateCodes.EnquiryReceived, NotificationChannel.Sms, "en", null,
            "Dear {{GuardianName}}, thank you for your enquiry about {{ChildName}} at {{CampusName}}. Ref {{EnquiryNo}}. Our admission team will call you soon.");
        T(TemplateCodes.EnquiryReceived, NotificationChannel.Sms, "bn", null,
            "প্রিয় {{GuardianName}}, {{CampusName}}-এ {{ChildName}}-এর ভর্তি বিষয়ে যোগাযোগের জন্য ধন্যবাদ। রেফ {{EnquiryNo}}। আমাদের ভর্তি দল শীঘ্রই যোগাযোগ করবে।");

        T(TemplateCodes.ApplicationSubmitted, NotificationChannel.Sms, "en", null,
            "Application {{ApplicationNo}} for {{ChildName}} has been received. We will inform you of the admission test date.");
        T(TemplateCodes.ApplicationSubmitted, NotificationChannel.Sms, "bn", null,
            "{{ChildName}}-এর আবেদন {{ApplicationNo}} গৃহীত হয়েছে। ভর্তি পরীক্ষার তারিখ জানানো হবে।");

        T(TemplateCodes.TestScheduled, NotificationChannel.Sms, "en", null,
            "Admission test for {{ChildName}}: {{TestDate}} at {{Room}}, {{CampusName}}. Admit card no {{AdmitCardNo}}.");
        T(TemplateCodes.TestScheduled, NotificationChannel.Sms, "bn", null,
            "{{ChildName}}-এর ভর্তি পরীক্ষা: {{TestDate}}, {{Room}}, {{CampusName}}। প্রবেশপত্র নং {{AdmitCardNo}}।");

        T(TemplateCodes.OfferIssued, NotificationChannel.Sms, "en", null,
            "Congratulations! {{ChildName}} has been offered a seat in {{ClassName}} at {{CampusName}}. Respond by {{ExpiresOn}}: {{ResponseLink}}");
        T(TemplateCodes.OfferIssued, NotificationChannel.Sms, "bn", null,
            "অভিনন্দন! {{ChildName}} {{CampusName}}-এ {{ClassName}}-এ ভর্তির সুযোগ পেয়েছে। {{ExpiresOn}}-এর মধ্যে জানান: {{ResponseLink}}");
        T(TemplateCodes.OfferIssued, NotificationChannel.Email, "en", "Offer of admission — {{ChildName}}",
            "<p>Dear {{GuardianName}},</p><p>We are pleased to offer {{ChildName}} a place in <b>{{ClassName}}</b> at {{CampusName}} for {{AcademicYear}}.</p><p>{{FeeSchedule}}</p><p>Please accept or decline by <b>{{ExpiresOn}}</b>: <a href=\"{{ResponseLink}}\">{{ResponseLink}}</a></p><p>Admissions Office<br/>{{SchoolName}}</p>");

        T(TemplateCodes.AdmissionInvoiceIssued, NotificationChannel.Sms, "en", null,
            "Admission invoice {{InvoiceNo}} for {{ChildName}}: BDT {{Amount}}, due {{DueDate}}.");
        T(TemplateCodes.AdmissionInvoiceIssued, NotificationChannel.Sms, "bn", null,
            "{{ChildName}}-এর ভর্তি ইনভয়েস {{InvoiceNo}}: ৳{{Amount}}, শেষ তারিখ {{DueDate}}।");

        T(TemplateCodes.PaymentReceived, NotificationChannel.Sms, "en", null,
            "Payment of BDT {{Amount}} received for {{InvoiceNo}} ({{ChildName}}). Ref {{Reference}}. Thank you.");
        T(TemplateCodes.PaymentReceived, NotificationChannel.Sms, "bn", null,
            "{{InvoiceNo}} ({{ChildName}})-এর ৳{{Amount}} পরিশোধ গৃহীত হয়েছে। রেফ {{Reference}}। ধন্যবাদ।");

        T(TemplateCodes.EnrolmentWelcome, NotificationChannel.Sms, "en", null,
            "Welcome to {{SchoolName}}! {{ChildName}} is enrolled in {{ClassName}}, admission no {{AdmissionNo}}. Parent login: {{UserName}} / {{Password}} at {{PortalUrl}}");
        T(TemplateCodes.EnrolmentWelcome, NotificationChannel.Sms, "bn", null,
            "{{SchoolName}}-এ স্বাগতম! {{ChildName}} {{ClassName}}-এ ভর্তি হয়েছে, ভর্তি নং {{AdmissionNo}}। অভিভাবক লগইন: {{UserName}} / {{Password}} — {{PortalUrl}}");

        T(TemplateCodes.WaitlistPromoted, NotificationChannel.Sms, "en", null,
            "Good news: a seat is now available for {{ChildName}} in {{ClassName}}. You will receive an offer shortly.");
    }
}

public static class SettingKeys
{
    public const string RequirePaymentBeforeEnrolment = "Admission:RequirePaymentBeforeEnrolment";
    public const string OfferValidityDays = "Admission:OfferValidityDays";
    public const string TestWeight = "Admission:TestWeight";
    public const string InterviewWeight = "Admission:InterviewWeight";
    public const string SiblingBonus = "Admission:SiblingBonus";
    public const string StaffWardBonus = "Admission:StaffWardBonus";
    public const string IdCardReissueFee = "IdCard:ReissueFee";
    public const string SmsSenderId = "Sms:SenderId";
    public const string SchoolPortalUrl = "Portal:Url";
}

public static class TemplateCodes
{
    public const string EnquiryReceived = "ADM_ENQUIRY_RECEIVED";
    public const string ApplicationSubmitted = "ADM_APPLICATION_SUBMITTED";
    public const string TestScheduled = "ADM_TEST_SCHEDULED";
    public const string OfferIssued = "ADM_OFFER_ISSUED";
    public const string AdmissionInvoiceIssued = "ADM_INVOICE_ISSUED";
    public const string PaymentReceived = "PAYMENT_RECEIVED";
    public const string EnrolmentWelcome = "SIS_ENROLMENT_WELCOME";
    public const string WaitlistPromoted = "ADM_WAITLIST_PROMOTED";
}
