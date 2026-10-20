using SchoolManagement.Domain.Core;
using SchoolManagement.Infrastructure.Localization;
using SchoolManagement.Infrastructure.Numbering;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-07: system settings (SMS sender ID, letterheads, policy switches) and numbering formats.</summary>
[ApiController]
[Route("api/settings")]
[Tags("Core: Settings")]
public sealed class SettingsController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Settings.View)]
    public async Task<List<SettingView>> List([FromQuery] Guid? campusId, CancellationToken ct) =>
        await db.SystemSettings.AsNoTracking().Where(s => campusId == null ? s.CampusId == null : s.CampusId == campusId)
            .OrderBy(s => s.Key).Select(s => new SettingView(s.Key, s.CampusId, s.Value, s.Description)).ToListAsync(ct);

    /// <summary>Creates or updates a setting. Set <c>campusId</c> to override the organisation-wide value for one campus.</summary>
    [HttpPut]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<SettingView> Upsert(SettingRequest request, CancellationToken ct)
    {
        var s = await db.SystemSettings.FirstOrDefaultAsync(x => x.Key == request.Key && x.CampusId == request.CampusId, ct);
        if (s is null)
        {
            s = new SystemSetting { Key = request.Key, CampusId = request.CampusId };
            db.SystemSettings.Add(s);
        }
        s.Value = request.Value;
        s.Description = request.Description ?? s.Description;
        await db.SaveChangesAsync(ct);
        return new SettingView(s.Key, s.CampusId, s.Value, s.Description);
    }

    [HttpGet("numbering")]
    [HasPermission(Permissions.Settings.View)]
    public async Task<List<NumberingView>> Numbering(CancellationToken ct) =>
        await db.NumberSequences.AsNoTracking().OrderBy(n => n.Key).ThenBy(n => n.CampusId)
            .Select(n => new NumberingView(n.Id, n.Key, n.CampusId, n.AcademicYearId, n.Prefix, n.Pattern, n.NextValue)).ToListAsync(ct);

    /// <summary>
    /// Changes a numbering format. Tokens: {PREFIX} {CAMPUS} {YEAR} {YY} {SEQ:n}. <c>NextValue</c> can only move forward
    /// so numbers are never reused.
    /// </summary>
    [HttpPut("numbering/{id:guid}")]
    [HasPermission(Permissions.Settings.Manage)]
    public async Task<NumberingView> UpdateNumbering(Guid id, NumberingRequest request, CancellationToken ct)
    {
        var n = await db.NumberSequences.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Number sequence", id);
        if (request.NextValue is { } next)
        {
            if (next < n.NextValue) throw new DomainException("The next value cannot be moved backwards.", "sequence_backwards");
            n.NextValue = next;
        }
        n.Prefix = request.Prefix;
        n.Pattern = request.Pattern;
        n.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return new NumberingView(n.Id, n.Key, n.CampusId, n.AcademicYearId, n.Prefix, n.Pattern, n.NextValue);
    }

    /// <summary>Previews a pattern without consuming a number.</summary>
    [HttpGet("numbering/preview")]
    [HasPermission(Permissions.Settings.View)]
    public string PreviewNumbering([FromQuery] string pattern, [FromQuery] string? prefix, [FromQuery] string? campusCode, [FromQuery] long value = 1) =>
        NumberSequenceService.Format(pattern, prefix ?? "", campusCode, clock.GetLocalNow().Year, value);

    /// <summary>CORE-06: today in Gregorian, Hijri and Bangla digits, plus a lakh/crore formatting sample.</summary>
    [HttpGet("/api/localization/today")]
    [AllowAnonymous]
    public LocalizationToday Today([FromQuery] int hijriAdjustDays = 0, [FromQuery] decimal? amount = null)
    {
        var now = BanglaFormat.ToDhaka(clock.GetUtcNow().UtcDateTime);
        var date = DateOnly.FromDateTime(now);
        var amountText = BanglaFormat.FormatBdt(amount ?? 660000m);
        return new LocalizationToday(date, now.ToString("dd MMM yyyy, HH:mm"), BanglaFormat.ToBanglaDigits(now.ToString("dd/MM/yyyy")),
            BanglaFormat.ToHijri(date, hijriAdjustDays), amountText, BanglaFormat.ToBanglaDigits(amountText));
    }
}

public record SettingView(string Key, Guid? CampusId, string? Value, string? Description);
public record SettingRequest(string Key, Guid? CampusId, string? Value, string? Description);
public record NumberingView(Guid Id, string Key, Guid? CampusId, Guid? AcademicYearId, string Prefix, string Pattern, long NextValue);
public record NumberingRequest(string Prefix, string Pattern, long? NextValue);
public record LocalizationToday(DateOnly Date, string DhakaTime, string DateBangla, string Hijri, string AmountSample, string AmountSampleBangla);

public sealed class SettingRequestValidator : AbstractValidator<SettingRequest>
{
    public SettingRequestValidator() => RuleFor(x => x.Key).NotEmpty().MaximumLength(128);
}

public sealed class NumberingRequestValidator : AbstractValidator<NumberingRequest>
{
    public NumberingRequestValidator()
    {
        RuleFor(x => x.Pattern).NotEmpty().MaximumLength(128).Must(p => p.Contains("{SEQ")).WithMessage("Pattern must contain {SEQ} or {SEQ:n}.");
        RuleFor(x => x.Prefix).NotNull().MaximumLength(32);
    }
}
