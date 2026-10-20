using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Domain.Core;
using SchoolManagement.Infrastructure.Persistence;

namespace SchoolManagement.Infrastructure.Numbering;

public static class NumberKeys
{
    public const string Enquiry = "enquiry";
    public const string Application = "application";
    public const string AdmitCard = "admit-card";
    public const string Offer = "offer";
    public const string AdmissionInvoice = "admission-invoice";
    public const string AdmissionNo = "admission-no";
    public const string Family = "family";
    public const string IdCard = "id-card";
    public const string TransferCertificate = "transfer-certificate";
}

public interface INumberSequenceService
{
    /// <summary>
    /// Returns the next formatted number for <paramref name="key"/>. Runs in its own unit of work so the
    /// caller's pending changes are not flushed; numbers consumed by a failed transaction are simply skipped.
    /// </summary>
    Task<string> NextAsync(string key, Guid? campusId = null, Guid? academicYearId = null, CancellationToken ct = default);
}

public sealed partial class NumberSequenceService(IServiceScopeFactory scopeFactory, TimeProvider clock) : INumberSequenceService
{
    private const int MaxAttempts = 10;

    public async Task<string> NextAsync(string key, Guid? campusId = null, Guid? academicYearId = null, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var _ = db.ElevateToSystem();

            var seq = await db.NumberSequences.SingleOrDefaultAsync(
                s => s.Key == key && s.CampusId == campusId && s.AcademicYearId == academicYearId, ct);

            if (seq is null)
            {
                // A row with no campus/year acts as the template for prefix and pattern.
                var template = await db.NumberSequences.AsNoTracking()
                    .SingleOrDefaultAsync(s => s.Key == key && s.CampusId == null && s.AcademicYearId == null, ct);
                if (template is not null && (campusId is not null || academicYearId is not null))
                {
                    seq = new NumberSequence { Key = key, CampusId = campusId, AcademicYearId = academicYearId, Prefix = template.Prefix, Pattern = template.Pattern };
                }
                else
                {
                    seq = new NumberSequence { Key = key, CampusId = campusId, AcademicYearId = academicYearId };
                }
                db.NumberSequences.Add(seq);
            }

            var value = seq.NextValue;
            seq.NextValue++;
            seq.Version = Guid.NewGuid();

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // Another request took the number (concurrency token) or created the row first (unique index).
                continue;
            }

            string? campusCode = null;
            if (campusId is not null)
                campusCode = await db.Campuses.Where(c => c.Id == campusId).Select(c => c.Code).FirstOrDefaultAsync(ct);

            int? yearStart = null;
            if (academicYearId is not null)
                yearStart = await db.AcademicYears.Where(y => y.Id == academicYearId).Select(y => (int?)y.StartDate.Year).FirstOrDefaultAsync(ct);

            return Format(seq.Pattern, seq.Prefix, campusCode, yearStart ?? clock.GetLocalNow().Year, value);
        }
    }

    public static string Format(string pattern, string prefix, string? campusCode, int year, long value)
    {
        var result = pattern
            .Replace("{PREFIX}", prefix)
            .Replace("{CAMPUS}", campusCode ?? "")
            .Replace("{YEAR}", year.ToString("D4"))
            .Replace("{YY}", (year % 100).ToString("D2"));
        return SeqToken().Replace(result, m =>
        {
            var width = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1;
            return value.ToString().PadLeft(width, '0');
        });
    }

    [GeneratedRegex(@"\{SEQ(?::(\d+))?\}")]
    private static partial Regex SeqToken();
}
