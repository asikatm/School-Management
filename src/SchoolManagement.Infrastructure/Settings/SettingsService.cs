using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SchoolManagement.Infrastructure.Persistence;

namespace SchoolManagement.Infrastructure.Settings;

/// <summary>Reads <c>SystemSetting</c> values; a campus-specific row overrides the organisation-wide one.</summary>
public interface ISettingsService
{
    Task<string?> GetAsync(string key, Guid? campusId = null, CancellationToken ct = default);
    Task<bool> GetBoolAsync(string key, bool fallback, Guid? campusId = null, CancellationToken ct = default);
    Task<decimal> GetDecimalAsync(string key, decimal fallback, Guid? campusId = null, CancellationToken ct = default);
    Task<int> GetIntAsync(string key, int fallback, Guid? campusId = null, CancellationToken ct = default);
}

public sealed class SettingsService(AppDbContext db) : ISettingsService
{
    public async Task<string?> GetAsync(string key, Guid? campusId = null, CancellationToken ct = default)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == key && (s.CampusId == null || s.CampusId == campusId))
            .Select(s => new { s.CampusId, s.Value })
            .ToListAsync(ct);
        return rows.FirstOrDefault(r => r.CampusId != null)?.Value ?? rows.FirstOrDefault()?.Value;
    }

    public async Task<bool> GetBoolAsync(string key, bool fallback, Guid? campusId = null, CancellationToken ct = default) =>
        bool.TryParse(await GetAsync(key, campusId, ct), out var v) ? v : fallback;

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, Guid? campusId = null, CancellationToken ct = default) =>
        decimal.TryParse(await GetAsync(key, campusId, ct), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public async Task<int> GetIntAsync(string key, int fallback, Guid? campusId = null, CancellationToken ct = default) =>
        int.TryParse(await GetAsync(key, campusId, ct), out var v) ? v : fallback;
}
