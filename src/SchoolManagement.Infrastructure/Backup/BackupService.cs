using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SchoolManagement.Domain.Core;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Persistence;

namespace SchoolManagement.Infrastructure.Backup;

public class BackupOptions
{
    /// <summary>Folder the database server can write to. Empty = SQL Server's default backup folder.</summary>
    public string? Directory { get; set; }

    /// <summary>Local hour (Asia/Dhaka) after which the daily backup runs.</summary>
    public int DailyHourLocal { get; set; } = 2;
    public int RetentionDays { get; set; } = 30;
    public bool Enabled { get; set; } = true;
    public string PgDumpPath { get; set; } = "pg_dump";
}

public interface IBackupService
{
    Task<BackupRecord> RunAsync(string trigger, CancellationToken ct);
    Task<(bool Ok, string Message)> VerifyAsync(Guid backupId, CancellationToken ct);
}

/// <summary>CORE-13: full database backups with checksum and a restore-verification check.</summary>
public sealed class BackupService(
    AppDbContext db,
    IOptions<BackupOptions> options,
    IOptions<DatabaseOptions> dbOptions,
    TimeProvider clock,
    ILogger<BackupService> logger) : IBackupService
{
    public async Task<BackupRecord> RunAsync(string trigger, CancellationToken ct)
    {
        var record = new BackupRecord { StartedAtUtc = clock.GetUtcNow().UtcDateTime, Status = BackupStatus.Running, Trigger = trigger };
        db.BackupRecords.Add(record);
        await db.SaveChangesAsync(ct);

        try
        {
            var stamp = clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd_HHmmss");
            record.FilePath = dbOptions.Value.Provider == DatabaseProvider.PostgreSql
                ? await BackupPostgresAsync(stamp, ct)
                : await BackupSqlServerAsync(stamp, ct);

            if (File.Exists(record.FilePath))
            {
                record.SizeBytes = new FileInfo(record.FilePath).Length;
                await using var fs = File.OpenRead(record.FilePath);
                record.Sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
            }
            record.Status = BackupStatus.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Database backup failed");
            record.Status = BackupStatus.Failed;
            record.Error = ex.Message;
        }

        record.CompletedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        ApplyRetention();
        return record;
    }

    public async Task<(bool Ok, string Message)> VerifyAsync(Guid backupId, CancellationToken ct)
    {
        var record = await db.BackupRecords.FindAsync([backupId], ct);
        if (record?.FilePath is null) return (false, "Backup not found.");

        if (dbOptions.Value.Provider == DatabaseProvider.PostgreSql)
        {
            if (!File.Exists(record.FilePath)) return (false, "Backup file is missing.");
            await using var fs = File.OpenRead(record.FilePath);
            var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct));
            return sha == record.Sha256 ? (true, "Checksum matches.") : (false, "Checksum mismatch.");
        }

        await using var conn = new SqlConnection(db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM", conn) { CommandTimeout = 0 };
        cmd.Parameters.AddWithValue("@path", record.FilePath);
        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
            return (true, "RESTORE VERIFYONLY succeeded; the backup set is valid.");
        }
        catch (SqlException ex)
        {
            return (false, ex.Message);
        }
    }

    private async Task<string> BackupSqlServerAsync(string stamp, CancellationToken ct)
    {
        var builder = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
        var database = builder.InitialCatalog;
        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);

        var dir = options.Value.Directory;
        if (string.IsNullOrWhiteSpace(dir))
        {
            await using var q = new SqlCommand("SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000))", conn);
            dir = (string)(await q.ExecuteScalarAsync(ct))!;
        }
        var path = Path.Combine(dir, $"{database}_{stamp}.bak");

        await using var cmd = new SqlCommand(
            $"BACKUP DATABASE {QuoteName(database)} TO DISK = @path WITH INIT, CHECKSUM, NAME = @name", conn)
        { CommandTimeout = 0 };
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@name", $"{database} full backup {stamp}");
        await cmd.ExecuteNonQueryAsync(ct);
        return path;
    }

    private async Task<string> BackupPostgresAsync(string stamp, CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var dir = options.Value.Directory ?? Path.Combine(AppContext.BaseDirectory, "backups");
        System.IO.Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{builder.Database}_{stamp}.dump");

        var psi = new ProcessStartInfo(options.Value.PgDumpPath,
            ["-h", builder.Host ?? "localhost", "-p", builder.Port.ToString(), "-U", builder.Username ?? "postgres", "-F", "c", "-f", path, builder.Database ?? ""])
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["PGPASSWORD"] = builder.Password ?? "";
        using var process = Process.Start(psi)!;
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"pg_dump failed ({process.ExitCode}): {error}");
        return path;
    }

    private void ApplyRetention()
    {
        var dir = options.Value.Directory;
        if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir)) return;
        var cutoff = DateTime.UtcNow.AddDays(-options.Value.RetentionDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*.*")
                     .Where(f => f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dump", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (File.GetCreationTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete old backup {File}", file);
            }
        }
    }

    private static string QuoteName(string name) => "[" + name.Replace("]", "]]") + "]";
}

/// <summary>Runs one scheduled backup per day after the configured local hour.</summary>
public sealed class DailyBackupJob : IScheduledJob
{
    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    public string Name => "daily-backup";
    public TimeSpan Interval => TimeSpan.FromMinutes(10);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var options = services.GetRequiredService<IOptions<BackupOptions>>().Value;
        if (!options.Enabled) return;

        var nowUtc = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, Dhaka);
        if (local.Hour < options.DailyHourLocal) return;

        var startOfLocalDayUtc = TimeZoneInfo.ConvertTimeToUtc(local.Date, Dhaka);
        var db = services.GetRequiredService<AppDbContext>();
        var done = await db.BackupRecords.AnyAsync(b =>
            b.Trigger == "scheduled" && b.StartedAtUtc >= startOfLocalDayUtc && b.Status != BackupStatus.Failed, ct);
        if (done) return;

        await services.GetRequiredService<IBackupService>().RunAsync("scheduled", ct);
    }
}
