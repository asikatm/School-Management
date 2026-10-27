using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Approvals;
using SchoolManagement.Domain.Attendance;
using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Identity;
using SchoolManagement.Domain.Learning;
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
        if (!await db.Organisations.IgnoreQueryFilters().AnyAsync(ct))
            await SeedReferenceDataAsync(ct);
        // Later modules seed their own defaults into databases created by earlier releases.
        await SeedAcademicsAsync(ct);
        await SeedAttendanceAsync(ct);
        await SeedLearningAndExamsAsync(ct);
    }

    /// <summary>LRN and EXM defaults: gradebook categories and weights, exam and homework rules, and their message templates.</summary>
    private async Task SeedLearningAndExamsAsync(CancellationToken ct)
    {
        if (!await db.Campuses.AnyAsync(ct)) return;

        if (!await db.GradebookCategories.IgnoreQueryFilters().AnyAsync(ct))
        {
            var seq = 0;
            foreach (var (name, weight) in new[] { ("Classwork", 30m), ("Homework", 20m), ("Quizzes", 20m), ("Projects", 15m), ("Practicals", 15m) })
                db.GradebookCategories.Add(new GradebookCategory { Name = name, WeightPercent = weight, Sequence = seq++ });
        }

        await EnsureSettingAsync(SettingKeys.HomeworkReminderTime, "16:00", "LRN-02: the 'due tomorrow' homework reminder goes out after this time (local)", ct);
        await EnsureSettingAsync(SettingKeys.HomeworkLatePenaltyPercent, "10", "LRN-02: default late-submission penalty (% of the marks) for new homework", ct);
        await EnsureSettingAsync(SettingKeys.ExamDoubleEntry, "true", "EXM-03: marks must be entered twice (by different people) and match before approval", ct);
        await EnsureSettingAsync(SettingKeys.ExamShowRank, "true", "EXM-06: show section/class rank and percentile on report cards", ct);
        await EnsureSettingAsync(SettingKeys.ExamGpaZeroOnFail, "true", "EXM-04: on the GPA 5.00 scale a failed subject makes the overall GPA 0.00", ct);
        await EnsureSettingAsync(SettingKeys.ExamMaxFailedForPromotion, "2", "SIS-05: more failed subjects than this in the final exam suggests repeating the year", ct);
        await EnsureSettingAsync(SettingKeys.PrincipalName, "", "EXM-05: name printed under the principal's signature on report cards", ct);

        await EnsureTemplateAsync(TemplateCodes.HomeworkAssigned, NotificationChannel.InApp, "en", "New homework: {{Subject}}",
            "{{ChildName}} ({{ClassName}}) has new {{Subject}} homework: {{Title}}. Due {{Due}}.", ct);
        await EnsureTemplateAsync(TemplateCodes.HomeworkAssigned, NotificationChannel.InApp, "bn", "নতুন বাড়ির কাজ: {{Subject}}",
            "{{ChildName}} ({{ClassName}})-এর নতুন {{Subject}} বাড়ির কাজ: {{Title}}। জমার সময় {{Due}}।", ct);
        await EnsureTemplateAsync(TemplateCodes.HomeworkDue, NotificationChannel.InApp, "en", "Homework due tomorrow",
            "Reminder: {{ChildName}}'s {{Subject}} homework \"{{Title}}\" is due {{Due}} and has not been handed in yet.", ct);
        await EnsureTemplateAsync(TemplateCodes.HomeworkDue, NotificationChannel.InApp, "bn", "আগামীকাল বাড়ির কাজ জমা",
            "মনে করিয়ে দিচ্ছি: {{ChildName}}-এর {{Subject}} বাড়ির কাজ \"{{Title}}\" {{Due}}-এর মধ্যে জমা দিতে হবে, এখনও জমা হয়নি।", ct);
        await EnsureTemplateAsync(TemplateCodes.ExamSchedulePublished, NotificationChannel.Sms, "en", null,
            "{{Exam}} for {{ChildName}} ({{ClassName}}) starts {{Start}}. The date sheet and admit card are in the parent portal.", ct);
        await EnsureTemplateAsync(TemplateCodes.ExamSchedulePublished, NotificationChannel.Sms, "bn", null,
            "{{ChildName}} ({{ClassName}})-এর {{Exam}} শুরু {{Start}}। রুটিন ও প্রবেশপত্র অভিভাবক পোর্টালে দেখুন।", ct);
        await EnsureTemplateAsync(TemplateCodes.ExamSchedulePublished, NotificationChannel.InApp, "en", "Exam schedule: {{Exam}}",
            "{{Exam}} for {{ChildName}} starts {{Start}}. See the date sheet and admit card.", ct);
        await EnsureTemplateAsync(TemplateCodes.ResultPublished, NotificationChannel.Sms, "en", null,
            "{{Exam}} results of {{ChildName}} ({{ClassName}}) are published: {{Summary}}. See the report card in the parent portal.", ct);
        await EnsureTemplateAsync(TemplateCodes.ResultPublished, NotificationChannel.Sms, "bn", null,
            "{{ChildName}} ({{ClassName}})-এর {{Exam}} ফলাফল প্রকাশিত: {{Summary}}। রিপোর্ট কার্ড অভিভাবক পোর্টালে দেখুন।", ct);
        await EnsureTemplateAsync(TemplateCodes.ResultPublished, NotificationChannel.InApp, "en", "Results: {{Exam}}",
            "{{Exam}} results of {{ChildName}} are published: {{Summary}}.", ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureSettingAsync(string key, string value, string description, CancellationToken ct)
    {
        if (!await db.SystemSettings.AnyAsync(s => s.Key == key && s.CampusId == null, ct) && !db.SystemSettings.Local.Any(s => s.Key == key && s.CampusId == null))
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = description });
    }

    private async Task EnsureTemplateAsync(string code, NotificationChannel channel, string lang, string? subject, string body, CancellationToken ct)
    {
        if (!await db.MessageTemplates.AnyAsync(t => t.Code == code && t.Channel == channel && t.Language == lang, ct))
            db.MessageTemplates.Add(new MessageTemplate { Code = code, Channel = channel, Language = lang, Subject = subject, Body = body });
    }

    /// <summary>ATT defaults: leave types, the staff-leave approval chain, alert templates and attendance rules.</summary>
    private async Task SeedAttendanceAsync(CancellationToken ct)
    {
        if (!await db.Campuses.AnyAsync(ct)) return;

        if (!await db.LeaveTypes.IgnoreQueryFilters().AnyAsync(ct))
        {
            foreach (var (code, name, bn, days, paid, doc) in new[]
                     {
                         ("CL", "Casual leave", "নৈমিত্তিক ছুটি", 10m, true, false),
                         ("SL", "Sick leave", "অসুস্থতাজনিত ছুটি", 14m, true, true),
                         ("EL", "Earned leave", "অর্জিত ছুটি", 20m, true, false),
                         ("ML", "Maternity leave", "মাতৃত্বকালীন ছুটি", 112m, true, true),
                         ("UL", "Leave without pay", "বিনা বেতনে ছুটি", 0m, false, false)
                     })
                db.LeaveTypes.Add(new LeaveType { Code = code, Name = name, NameBn = bn, DaysPerYear = days, IsPaid = paid, RequiresDocument = doc });
        }

        if (!await db.ApprovalWorkflows.AnyAsync(w => w.RequestType == ApprovalTypes.StaffLeave, ct))
        {
            var roles = await db.Roles.ToDictionaryAsync(r => r.Name, r => r.Id, ct);
            var wf = new ApprovalWorkflow { RequestType = ApprovalTypes.StaffLeave, Name = "Staff leave" };
            wf.Steps.Add(new ApprovalStep
            {
                WorkflowId = wf.Id, Order = 1, Name = "Head of Section approval", ApproverRoleId = roles[RoleNames.HeadOfSection],
                EscalateAfterHours = 24, EscalateToRoleId = roles[RoleNames.Principal]
            });
            db.ApprovalWorkflows.Add(wf);
        }

        await EnsureSettingAsync(SettingKeys.AttendanceAlertCutoff, "09:30", "ATT-03: absence/late SMS go out after this time (local), so late arrivals can be marked first", ct);
        await EnsureSettingAsync(SettingKeys.AttendanceSendAlerts, "true", "ATT-03: send absence and late alerts to guardians", ct);
        await EnsureSettingAsync(SettingKeys.AttendanceEditWindowHours, "48", "ATT-01: hours after the day in which the teacher who took the register may still change it", ct);
        await EnsureSettingAsync(SettingKeys.AttendanceWarningPercent, "85", "ATT-05: guardians are warned when term attendance falls below this %", ct);
        await EnsureSettingAsync(SettingKeys.AttendanceMinimumPercent, "75", "ATT-05: minimum term attendance % for exam eligibility", ct);
        await EnsureSettingAsync(SettingKeys.PeriodWiseProgrammes, "IGCSE,IAL", "ATT-01: programme codes whose classes also take period-wise attendance", ct);
        await EnsureSettingAsync(SettingKeys.StaffWorkStart, "07:30", "ATT-06: staff start time (local)", ct);
        await EnsureSettingAsync(SettingKeys.StaffWorkEnd, "14:30", "ATT-06: staff finish time (local)", ct);
        await EnsureSettingAsync(SettingKeys.StaffLateGraceMinutes, "10", "ATT-06: minutes after the start time before a punch counts as late", ct);
        await EnsureSettingAsync(SettingKeys.StaffOvertimeAfterMinutes, "30", "ATT-06: minutes after the finish time before overtime is counted", ct);
        await EnsureSettingAsync(SettingKeys.StaffGeofence, "", "ATT-06: campus location for mobile punch as 'latitude,longitude,radius metres' (set per campus; empty = no check)", ct);

        await EnsureTemplateAsync(TemplateCodes.StudentAbsent, NotificationChannel.Sms, "en", null,
            "{{ChildName}} ({{ClassName}}) is absent from school today, {{Date}}. Please inform the class teacher if this is unexpected.", ct);
        await EnsureTemplateAsync(TemplateCodes.StudentAbsent, NotificationChannel.Sms, "bn", null,
            "{{ChildName}} ({{ClassName}}) আজ {{Date}} স্কুলে অনুপস্থিত। অপ্রত্যাশিত হলে শ্রেণি শিক্ষককে জানান।", ct);
        await EnsureTemplateAsync(TemplateCodes.StudentAbsent, NotificationChannel.InApp, "en", "Absent today",
            "{{ChildName}} ({{ClassName}}) is marked absent on {{Date}}.", ct);
        await EnsureTemplateAsync(TemplateCodes.StudentLate, NotificationChannel.Sms, "en", null,
            "{{ChildName}} ({{ClassName}}) arrived late today, {{Date}}{{Arrival}}.", ct);
        await EnsureTemplateAsync(TemplateCodes.StudentLate, NotificationChannel.Sms, "bn", null,
            "{{ChildName}} ({{ClassName}}) আজ {{Date}} দেরিতে উপস্থিত হয়েছে{{Arrival}}।", ct);
        await EnsureTemplateAsync(TemplateCodes.LowAttendance, NotificationChannel.Sms, "en", null,
            "Attendance alert: {{ChildName}} ({{ClassName}}) has {{Percent}}% attendance in {{Term}}. The minimum for examinations is {{Minimum}}%.", ct);
        await EnsureTemplateAsync(TemplateCodes.LowAttendance, NotificationChannel.Sms, "bn", null,
            "উপস্থিতি সতর্কতা: {{Term}}-এ {{ChildName}} ({{ClassName}})-এর উপস্থিতি {{Percent}}%। পরীক্ষায় অংশ নিতে ন্যূনতম {{Minimum}}% প্রয়োজন।", ct);
        await EnsureTemplateAsync(TemplateCodes.StudentLeaveDecided, NotificationChannel.Sms, "en", null,
            "Leave for {{ChildName}} ({{From}}–{{To}}) has been {{Decision}}. {{Note}}", ct);
        await EnsureTemplateAsync(TemplateCodes.StudentLeaveDecided, NotificationChannel.Sms, "bn", null,
            "{{ChildName}}-এর ছুটি ({{From}}–{{To}}) {{DecisionBn}}। {{Note}}", ct);
        await EnsureTemplateAsync(TemplateCodes.StudentLeaveDecided, NotificationChannel.InApp, "en", "Leave {{Decision}}",
            "Leave for {{ChildName}} ({{From}}–{{To}}) has been {{Decision}}. {{Note}}", ct);

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedReferenceDataAsync(CancellationToken ct)
    {
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
                Permissions.Students.Export, Permissions.Students.Documents,
                Permissions.Academic.View, Permissions.Academic.Manage, Permissions.Academic.Timetable, Permissions.Academic.Substitutions,
                Permissions.Academic.Calendar,
                Permissions.Attendance.View, Permissions.Attendance.Take, Permissions.Attendance.Edit, Permissions.Attendance.ApproveLeave,
                Permissions.Attendance.Staff, Permissions.StaffLeave.Manage,
                Permissions.Learning.View, Permissions.Learning.Teach, Permissions.Learning.Manage, Permissions.Learning.Moderate,
                Permissions.Exams.View, Permissions.Exams.Manage, Permissions.Exams.Marks, Permissions.Exams.Moderate, Permissions.Notices.Manage
            ],
            [RoleNames.HeadOfSection] =
            [
                Permissions.Settings.View, Permissions.Approvals.View, Permissions.Notifications.View, Permissions.Notifications.Send,
                Permissions.Admission.View, Permissions.Students.View, Permissions.Students.Edit, Permissions.Students.Export,
                Permissions.Academic.View, Permissions.Academic.Timetable, Permissions.Academic.Substitutions, Permissions.Academic.Calendar,
                Permissions.Attendance.View, Permissions.Attendance.Take, Permissions.Attendance.Edit, Permissions.Attendance.ApproveLeave,
                Permissions.Learning.View, Permissions.Learning.Teach, Permissions.Learning.Moderate,
                Permissions.Exams.View, Permissions.Exams.Marks, Permissions.Exams.Moderate, Permissions.Notices.Manage
            ],
            [RoleNames.ClassTeacher] =
            [
                Permissions.Settings.View, Permissions.Students.View, Permissions.Notifications.View, Permissions.Approvals.View, Permissions.Academic.View,
                Permissions.Attendance.View, Permissions.Attendance.Take,
                Permissions.Learning.View, Permissions.Learning.Teach, Permissions.Exams.View, Permissions.Exams.Marks
            ],
            [RoleNames.SubjectTeacher] =
            [
                Permissions.Settings.View, Permissions.Students.View, Permissions.Notifications.View, Permissions.Academic.View,
                Permissions.Attendance.View, Permissions.Attendance.Take,
                Permissions.Learning.View, Permissions.Learning.Teach, Permissions.Exams.View, Permissions.Exams.Marks
            ],
            [RoleNames.ExamOfficer] =
            [
                Permissions.Settings.View, Permissions.Students.View, Permissions.Academic.View, Permissions.Attendance.View,
                Permissions.Notifications.View, Permissions.Notifications.Send,
                Permissions.Exams.View, Permissions.Exams.Manage, Permissions.Exams.Moderate, Permissions.Notices.Manage
            ],
            [RoleNames.Accountant] =
            [
                Permissions.Settings.View, Permissions.Admission.View, Permissions.Students.View,
                Permissions.Notifications.View, Permissions.Notifications.Send, Permissions.Approvals.View, Permissions.Academic.View
            ],
            [RoleNames.AdmissionOfficer] =
            [
                Permissions.Settings.View, Permissions.Admission.View, Permissions.Admission.Create, Permissions.Admission.Edit,
                Permissions.Admission.Approve, Permissions.Admission.Export, Permissions.Students.View, Permissions.Students.Create,
                Permissions.Notifications.View, Permissions.Notifications.Send, Permissions.Academic.View
            ],
            [RoleNames.Librarian] = [Permissions.Students.View, Permissions.Academic.View],
            [RoleNames.TransportInCharge] = [Permissions.Students.View, Permissions.Academic.View],
            [RoleNames.Parent] = [Permissions.Portal.Parent],
            [RoleNames.Student] = [Permissions.Portal.Student]
        };

        var roles = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions).ToListAsync(ct);

        // Permissions added by later releases are handed to the default roles once. Anything already distributed is
        // left alone, so a permission the school removed from a role is never put back.
        var setting = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.DistributedPermissions && s.CampusId == null, ct);
        var distributed = setting?.Value?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
                          ?? roles.SelectMany(r => r.Permissions).Select(p => p.Permission).ToHashSet();
        var fresh = Permissions.All.Where(p => !distributed.Contains(p)).ToHashSet();

        foreach (var (name, permissions) in map)
        {
            var role = roles.FirstOrDefault(r => r.Name == name);
            if (role is null)
            {
                role = new Role { Name = name, IsSystem = true, Description = $"Default {name} role" };
                foreach (var p in permissions) role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
                db.Roles.Add(role);
                continue;
            }
            var toAdd = name == RoleNames.ItAdmin ? permissions : permissions.Where(fresh.Contains);
            foreach (var p in toAdd.Where(p => role.Permissions.All(rp => rp.Permission != p)))
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
        }

        if (setting is null)
        {
            setting = new SystemSetting { Key = SettingKeys.DistributedPermissions, Description = "Permissions already given to the default roles (managed by the system)" };
            db.SystemSettings.Add(setting);
        }
        setting.Value = string.Join(',', Permissions.All);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>ACA defaults: bell schedule, rooms, curriculum per class, timetable rules and the cover template.</summary>
    private async Task SeedAcademicsAsync(CancellationToken ct)
    {
        var campuses = await db.Campuses.Select(c => c.Id).ToListAsync(ct);
        if (campuses.Count == 0) return;

        if (!await db.PeriodDefinitions.AnyAsync(ct))
        {
            (string Name, int H1, int M1, int H2, int M2, bool Break)[] bell =
            [
                ("Period 1", 7, 45, 8, 25, false), ("Period 2", 8, 25, 9, 5, false), ("Period 3", 9, 5, 9, 45, false),
                ("Tiffin break", 9, 45, 10, 5, true),
                ("Period 4", 10, 5, 10, 45, false), ("Period 5", 10, 45, 11, 25, false), ("Period 6", 11, 25, 12, 5, false),
                ("Zuhr prayer & lunch", 12, 5, 12, 35, true),
                ("Period 7", 12, 35, 13, 15, false), ("Period 8", 13, 15, 13, 55, false)
            ];
            foreach (var campusId in campuses)
                for (var i = 0; i < bell.Length; i++)
                    db.PeriodDefinitions.Add(new PeriodDefinition
                    {
                        CampusId = campusId, PeriodNo = i + 1, Name = bell[i].Name, IsBreak = bell[i].Break,
                        StartTime = new TimeOnly(bell[i].H1, bell[i].M1), EndTime = new TimeOnly(bell[i].H2, bell[i].M2)
                    });
        }

        if (!await db.Rooms.AnyAsync(ct))
        {
            foreach (var campusId in campuses)
            {
                for (var n = 101; n <= 112; n++)
                    db.Rooms.Add(new Room { CampusId = campusId, Code = n.ToString(), Name = $"Room {n}", Type = RoomType.Classroom, Capacity = 30 });
                db.Rooms.Add(new Room { CampusId = campusId, Code = "SCI-LAB", Name = "Science Lab", Type = RoomType.ScienceLab, Capacity = 30 });
                db.Rooms.Add(new Room { CampusId = campusId, Code = "ICT-LAB", Name = "ICT Lab", Type = RoomType.ComputerLab, Capacity = 30 });
                db.Rooms.Add(new Room { CampusId = campusId, Code = "LIB", Name = "Library", Type = RoomType.Library, Capacity = 40 });
                db.Rooms.Add(new Room { CampusId = campusId, Code = "HALL", Name = "Assembly Hall", Type = RoomType.Hall, Capacity = 300 });
            }
        }

        if (!await db.CurriculumSubjects.AnyAsync(ct))
            await SeedCurriculumAsync(ct);

        foreach (var (key, value, description) in new[]
                 {
                     (SettingKeys.WorkingDays, "Saturday,Sunday,Monday,Tuesday,Wednesday,Thursday", "ACA-04: teaching days of the week"),
                     (SettingKeys.MaxTeacherPeriodsPerDay, "6", "ACA-04: warn when a teacher has more periods in a day"),
                     (SettingKeys.MaxTeacherPeriodsPerWeek, "30", "ACA-03: teacher load above this is shown as overloaded"),
                     (SettingKeys.MinTeacherPeriodsPerWeek, "12", "ACA-03: teacher load below this is shown as under-used"),
                     (SettingKeys.MaxConsecutivePeriods, "3", "ACA-04: warn when a teacher teaches more periods in a row")
                 })
        {
            if (!await db.SystemSettings.AnyAsync(s => s.Key == key && s.CampusId == null, ct))
                db.SystemSettings.Add(new SystemSetting { Key = key, Value = value, Description = description });
        }

        void Template(string code, NotificationChannel channel, string lang, string? subject, string body)
        {
            if (!db.MessageTemplates.Any(t => t.Code == code && t.Channel == channel && t.Language == lang))
                db.MessageTemplates.Add(new MessageTemplate { Code = code, Channel = channel, Language = lang, Subject = subject, Body = body });
        }

        Template(TemplateCodes.CoverAssigned, NotificationChannel.InApp, "en", "Cover assigned",
            "{{Date}}, {{PeriodName}} ({{Time}}): please cover {{SubjectName}} for {{ClassName}} in {{Room}}. {{Reason}}");
        Template(TemplateCodes.CoverAssigned, NotificationChannel.Sms, "en", null,
            "Cover duty {{Date}} {{PeriodName}} ({{Time}}): {{SubjectName}}, {{ClassName}}, {{Room}}.");
        Template(TemplateCodes.CoverAssigned, NotificationChannel.Sms, "bn", null,
            "বদলি ক্লাস {{Date}} {{PeriodName}} ({{Time}}): {{SubjectName}}, {{ClassName}}, {{Room}}।");

        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCurriculumAsync(CancellationToken ct)
    {
        var subjects = await db.Subjects.ToDictionaryAsync(s => s.Code, s => s.Id, ct);
        var yearGroups = await db.YearGroups.Include(y => y.Programme).ToListAsync(ct);
        if (subjects.Count == 0 || yearGroups.Count == 0) return;

        foreach (var yg in yearGroups)
        {
            var seq = 0;
            void Add(string code, SubjectType type, int periods, Guid? group = null, RoomType? room = null)
            {
                if (!subjects.TryGetValue(code, out var subjectId)) return;
                db.CurriculumSubjects.Add(new CurriculumSubject
                {
                    YearGroupId = yg.Id, SubjectId = subjectId, Type = type, PeriodsPerWeek = periods, SubjectGroupId = group,
                    PreferredRoomType = room, Sequence = seq++
                });
            }
            Guid Group(string name, int min, int max)
            {
                var g = new SubjectGroup { YearGroupId = yg.Id, Name = name, MinChoices = min, MaxChoices = max };
                db.SubjectGroups.Add(g);
                return g.Id;
            }

            switch (yg.Programme!.Code)
            {
                case "EY":
                    Add("ENG", SubjectType.Compulsory, 7); Add("MATH", SubjectType.Compulsory, 7); Add("BAN", SubjectType.Compulsory, 6);
                    Add("ISL", SubjectType.Compulsory, 3); Add("QUR", SubjectType.Compulsory, 3);
                    break;
                case "PRI":
                    Add("ENG", SubjectType.Compulsory, 7); Add("MATH", SubjectType.Compulsory, 7); Add("SCI", SubjectType.Compulsory, 5);
                    Add("BAN", SubjectType.Compulsory, 6); Add("ISL", SubjectType.Compulsory, 3); Add("QUR", SubjectType.Compulsory, 3);
                    Add("ICT", SubjectType.Compulsory, 2, room: RoomType.ComputerLab); Add("BGS", SubjectType.Compulsory, 3);
                    Add("ARB", SubjectType.Optional, 2, Group("Language option", 0, 1));
                    break;
                case "LSEC":
                    Add("ENG", SubjectType.Compulsory, 7); Add("MATH", SubjectType.Compulsory, 7);
                    Add("SCI", SubjectType.Compulsory, 6, room: RoomType.ScienceLab); Add("BAN", SubjectType.Compulsory, 5);
                    Add("ISL", SubjectType.Compulsory, 3); Add("QUR", SubjectType.Compulsory, 2);
                    Add("ICT", SubjectType.Compulsory, 3, room: RoomType.ComputerLab); Add("BGS", SubjectType.Compulsory, 3);
                    Add("ARB", SubjectType.Optional, 2, Group("Language option", 0, 1));
                    break;
                case "IGCSE":
                {
                    Add("ENG", SubjectType.Compulsory, 6); Add("MATH", SubjectType.Compulsory, 6); Add("BAN", SubjectType.Compulsory, 4);
                    Add("ISL", SubjectType.Compulsory, 3);
                    var options = Group("IGCSE options (choose 3–4)", 3, 4);
                    Add("PHY", SubjectType.Elective, 5, options, RoomType.ScienceLab); Add("CHEM", SubjectType.Elective, 5, options, RoomType.ScienceLab);
                    Add("BIO", SubjectType.Elective, 5, options, RoomType.ScienceLab); Add("ECO", SubjectType.Elective, 5, options);
                    Add("BUS", SubjectType.Elective, 5, options); Add("ACC", SubjectType.Elective, 5, options);
                    Add("CS", SubjectType.Elective, 5, options, RoomType.ComputerLab);
                    break;
                }
                case "IAL":
                {
                    Add("ISL", SubjectType.Compulsory, 2);
                    var options = Group("A Level subjects (choose 3–4)", 3, 4);
                    Add("MATH", SubjectType.Elective, 6, options); Add("PHY", SubjectType.Elective, 6, options, RoomType.ScienceLab);
                    Add("CHEM", SubjectType.Elective, 6, options, RoomType.ScienceLab); Add("BIO", SubjectType.Elective, 6, options, RoomType.ScienceLab);
                    Add("ECO", SubjectType.Elective, 6, options); Add("BUS", SubjectType.Elective, 6, options);
                    Add("ACC", SubjectType.Elective, 6, options); Add("CS", SubjectType.Elective, 6, options, RoomType.ComputerLab);
                    break;
                }
            }
        }
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
    public const string DistributedPermissions = "System:DistributedPermissions";
    public const string WorkingDays = "Timetable:WorkingDays";
    public const string MaxTeacherPeriodsPerDay = "Timetable:MaxTeacherPeriodsPerDay";
    public const string MaxTeacherPeriodsPerWeek = "Timetable:MaxTeacherPeriodsPerWeek";
    public const string MinTeacherPeriodsPerWeek = "Timetable:MinTeacherPeriodsPerWeek";
    public const string MaxConsecutivePeriods = "Timetable:MaxConsecutivePeriods";
    public const string AttendanceAlertCutoff = "Attendance:AlertCutoff";
    public const string AttendanceSendAlerts = "Attendance:SendAlerts";
    public const string AttendanceEditWindowHours = "Attendance:EditWindowHours";
    public const string AttendanceWarningPercent = "Attendance:WarningPercent";
    public const string AttendanceMinimumPercent = "Attendance:MinimumPercent";
    public const string PeriodWiseProgrammes = "Attendance:PeriodWiseProgrammes";
    public const string StaffWorkStart = "StaffAttendance:WorkStart";
    public const string StaffWorkEnd = "StaffAttendance:WorkEnd";
    public const string StaffLateGraceMinutes = "StaffAttendance:LateGraceMinutes";
    public const string StaffOvertimeAfterMinutes = "StaffAttendance:OvertimeAfterMinutes";
    public const string StaffGeofence = "StaffAttendance:Geofence";
    public const string HomeworkReminderTime = "Homework:ReminderTime";
    public const string HomeworkLatePenaltyPercent = "Homework:LatePenaltyPercent";
    public const string ExamDoubleEntry = "Exams:DoubleEntry";
    public const string ExamShowRank = "Exams:ShowRank";
    public const string ExamGpaZeroOnFail = "Exams:GpaZeroOnFail";
    public const string ExamMaxFailedForPromotion = "Exams:MaxFailedForPromotion";
    public const string PrincipalName = "ReportCard:PrincipalName";
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
    public const string CoverAssigned = "ACA_COVER_ASSIGNED";
    public const string StudentAbsent = "ATT_STUDENT_ABSENT";
    public const string StudentLate = "ATT_STUDENT_LATE";
    public const string LowAttendance = "ATT_LOW_ATTENDANCE";
    public const string StudentLeaveDecided = "ATT_LEAVE_DECIDED";
    public const string HomeworkAssigned = "LRN_HOMEWORK_ASSIGNED";
    public const string HomeworkDue = "LRN_HOMEWORK_DUE";
    public const string ExamSchedulePublished = "EXM_SCHEDULE_PUBLISHED";
    public const string ResultPublished = "EXM_RESULT_PUBLISHED";
}
