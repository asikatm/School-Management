using SchoolManagement.Domain.Core;
using SchoolManagement.Infrastructure.Backup;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-13: backup history, on-demand backups and restore verification.</summary>
[ApiController]
[Route("api/backups")]
[Tags("Core: Backup")]
public sealed class BackupController(AppDbContext db, IBackupService backups) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Backup.Manage)]
    public async Task<List<BackupRecord>> List(CancellationToken ct) =>
        await db.BackupRecords.AsNoTracking().OrderByDescending(b => b.StartedAtUtc).Take(100).ToListAsync(ct);

    [HttpPost]
    [HasPermission(Permissions.Backup.Manage)]
    public Task<BackupRecord> RunNow(CancellationToken ct) => backups.RunAsync("manual", ct);

    /// <summary>SQL Server: RESTORE VERIFYONLY WITH CHECKSUM. PostgreSQL: checksum of the dump file.</summary>
    [HttpPost("{id:guid}/verify")]
    [HasPermission(Permissions.Backup.Manage)]
    public async Task<BackupVerification> Verify(Guid id, CancellationToken ct)
    {
        var (ok, message) = await backups.VerifyAsync(id, ct);
        return new BackupVerification(ok, message);
    }
}

public record BackupVerification(bool Ok, string Message);
