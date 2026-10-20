using SchoolManagement.Api.Modules.Core;
using SchoolManagement.Api.Modules.Students.Services;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Domain.Core;
using SchoolManagement.Infrastructure.Auditing;
using SchoolManagement.Infrastructure.Localization;
using SchoolManagement.Infrastructure.Notifications;
using SchoolManagement.Infrastructure.Numbering;
using SchoolManagement.Infrastructure.Security;

namespace SchoolManagement.Tests;

public class NumberingTests
{
    [Theory]
    [InlineData("{PREFIX}{YY}{SEQ:5}", "APP", null, 2026, 42, "APP2600042")]
    [InlineData("{CAMPUS}{YY}{SEQ:4}", "", "DHN", 2026, 1, "DHN260001")]
    [InlineData("{PREFIX}-{CAMPUS}-{YEAR}-{SEQ}", "TC", "GUL", 2027, 7, "TC-GUL-2027-7")]
    [InlineData("{PREFIX}{SEQ:3}", "X", null, 2026, 12345, "X12345")]
    public void Format_replaces_every_token(string pattern, string prefix, string? campus, int year, long value, string expected) =>
        Assert.Equal(expected, NumberSequenceService.Format(pattern, prefix, campus, year, value));
}

public class BanglaFormatTests
{
    [Theory]
    [InlineData(660000, "6,60,000.00")]
    [InlineData(12345678.5, "1,23,45,678.50")]
    [InlineData(999, "999.00")]
    [InlineData(1000, "1,000.00")]
    [InlineData(-150000, "-1,50,000.00")]
    public void Groups_in_lakh_and_crore(decimal amount, string expected) =>
        Assert.Equal(expected, BanglaFormat.GroupLakhCrore(amount));

    [Fact]
    public void Converts_digits_to_bangla() =>
        Assert.Equal("৳৬,৬০,০০০.০০", BanglaFormat.FormatBdt(660000, bangla: true));

    [Fact]
    public void Hijri_date_for_a_known_day() =>
        // 1 Ramadan 1447 AH = 18 Feb 2026 in the Umm al-Qura calendar.
        Assert.Equal("1 Ramadan 1447 AH", BanglaFormat.ToHijri(new DateOnly(2026, 2, 18)));
}

public class GradingTests
{
    private static readonly GradeBand[] Igcse =
    [
        new() { Grade = "9", MinPercent = 90, MaxPercent = 100 },
        new() { Grade = "8", MinPercent = 80, MaxPercent = 89.99m },
        new() { Grade = "7", MinPercent = 70, MaxPercent = 79.99m },
        new() { Grade = "U", MinPercent = 0, MaxPercent = 69.99m, IsPass = false }
    ];

    [Theory]
    [InlineData(100, "9")]
    [InlineData(90, "9")]
    [InlineData(89.99, "8")]
    [InlineData(73.5, "7")]
    [InlineData(0, "U")]
    public void Finds_band_for_percentage(decimal percent, string grade) =>
        Assert.Equal(grade, GradeCalculator.Find(Igcse, percent)!.Grade);

    [Fact]
    public void Rejects_overlapping_and_incomplete_bands()
    {
        var errors = GradeCalculator.Validate(
        [
            new GradeBandRequest("A", null, null, 50, 100, null),
            new GradeBandRequest("B", null, null, 40, 60, null)
        ]);
        Assert.Contains(errors, e => e.Contains("overlap"));
        Assert.Contains(errors, e => e.Contains("start at 0"));
    }

    [Fact]
    public void Accepts_contiguous_two_decimal_bands() =>
        Assert.Empty(GradeCalculator.Validate(
        [
            new GradeBandRequest("Pass", null, null, 40, 100, null),
            new GradeBandRequest("Fail", null, null, 0, 39.99m, null, false)
        ]));
}

public class PhoneAndPasswordTests
{
    [Theory]
    [InlineData("01711000000", "+8801711000000")]
    [InlineData("+8801711000000", "+8801711000000")]
    [InlineData("8801911-000-000", "+8801911000000")]
    [InlineData("01211000000", null)]
    [InlineData("0171100000", null)]
    [InlineData("", null)]
    public void Normalises_bangladeshi_mobiles(string input, string? expected) =>
        Assert.Equal(expected, StudentImportService.NormalisePhone(input));

    [Theory]
    [InlineData("short1", false)]
    [InlineData("lettersonly", false)]
    [InlineData("password123", false)]
    [InlineData("Lighthouse2026", true)]
    public void Password_policy(string password, bool valid) =>
        Assert.Equal(valid, PasswordPolicy.Validate(password).Count == 0);

    [Fact]
    public void Bcrypt_round_trip()
    {
        var hasher = new BcryptPasswordHasher();
        var hash = hasher.Hash("Lighthouse2026");
        Assert.True(hasher.Verify("Lighthouse2026", hash));
        Assert.False(hasher.Verify("lighthouse2026", hash));
        Assert.False(hasher.Verify("x", "not-a-hash"));
    }
}

public class NotificationTests
{
    [Fact]
    public void Renders_merge_fields_and_blanks_unknown_ones() =>
        Assert.Equal("প্রিয় Karim, রেফ ENQ1. ",
            NotificationService.Render("প্রিয় {{GuardianName}}, রেফ {{ EnquiryNo }}. {{Missing}}",
                new Dictionary<string, string?> { ["GuardianName"] = "Karim", ["EnquiryNo"] = "ENQ1" }));

    [Theory]
    [InlineData(160, false, 1)]
    [InlineData(161, false, 2)]
    [InlineData(70, true, 1)]
    [InlineData(71, true, 2)]
    [InlineData(134, true, 2)]
    [InlineData(135, true, 3)]
    public void Counts_sms_segments(int length, bool bangla, int segments) =>
        Assert.Equal(segments, NotificationsController.SmsSegments(new string(bangla ? 'ক' : 'a', length)));
}

public class AuditChainTests
{
    [Fact]
    public void Hash_changes_when_content_or_previous_link_changes()
    {
        var e = new AuditEvent { Id = 1, OccurredAtUtc = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc), EntityType = "Student", EntityId = "x", Action = AuditAction.Update, NewValues = "{}" };
        var original = AuditChain.ComputeHash(e, AuditChain.Genesis);

        Assert.Equal(original, AuditChain.ComputeHash(e, AuditChain.Genesis));
        Assert.NotEqual(original, AuditChain.ComputeHash(e, "other"));
        e.NewValues = "{\"Mark\":99}";
        Assert.NotEqual(original, AuditChain.ComputeHash(e, AuditChain.Genesis));
    }
}
