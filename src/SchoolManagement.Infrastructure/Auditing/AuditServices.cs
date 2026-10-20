using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Persistence;
using SchoolManagement.Infrastructure.Security;

namespace SchoolManagement.Infrastructure.Auditing;

/// <summary>Writes audit events that are not entity changes: logins, exports, permission changes (CORE-05, spec 5.5).</summary>
public interface IAuditLog
{
    /// <summary>Adds the event to the current unit of work; it is saved with the caller's next SaveChanges.</summary>
    void Add(AuditAction action, string entityType, string? entityId, string? description, Guid? campusId = null);
}

public sealed class AuditLog(AppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLog
{
    public void Add(AuditAction action, string entityType, string? entityId, string? description, Guid? campusId = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
            UserId = currentUser.UserId,
            UserName = currentUser.UserName,
            IpAddress = currentUser.IpAddress,
            CampusId = campusId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Description = description
        });
    }
}

public static class AuditChain
{
    public const string Genesis = "GENESIS";

    public static string ComputeHash(AuditEvent e, string previousHash)
    {
        var payload = string.Join('|',
            previousHash, e.Id, e.OccurredAtUtc.ToString("O"), e.UserId, e.UserName, e.IpAddress, e.CampusId,
            (int)e.Action, e.EntityType, e.EntityId, e.OldValues, e.NewValues, e.Description);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}

public record AuditVerificationResult(bool IsIntact, long CheckedEvents, long UnsealedEvents, IReadOnlyList<string> Problems);

public interface IAuditVerifier
{
    Task<AuditVerificationResult> VerifyAsync(CancellationToken ct);
}

/// <summary>
/// Recomputes every sealed row's hash (detects edits) and checks every PreviousHash points at an existing
/// row (detects deletions).
/// </summary>
public sealed class AuditVerifier(AppDbContext db) : IAuditVerifier
{
    public async Task<AuditVerificationResult> VerifyAsync(CancellationToken ct)
    {
        var problems = new List<string>();
        var known = new HashSet<string> { AuditChain.Genesis };
        var previousLinks = new Dictionary<string, long>();
        long checkedCount = 0;
        long lastId = 0;
        const int batch = 2000;

        while (true)
        {
            var rows = await db.AuditEvents.AsNoTracking()
                .Where(e => e.Id > lastId && e.Hash != null)
                .OrderBy(e => e.Id).Take(batch).ToListAsync(ct);
            if (rows.Count == 0) break;

            foreach (var row in rows)
            {
                checkedCount++;
                if (AuditChain.ComputeHash(row, row.PreviousHash!) != row.Hash)
                    problems.Add($"Event {row.Id}: content does not match its hash (modified).");
                if (!previousLinks.TryAdd(row.PreviousHash!, row.Id))
                    problems.Add($"Event {row.Id}: shares its previous hash with event {previousLinks[row.PreviousHash!]} (chain forked).");
                known.Add(row.Hash!);
            }
            lastId = rows[^1].Id;
            if (problems.Count > 100) break;
        }

        foreach (var (previous, id) in previousLinks)
            if (!known.Contains(previous))
                problems.Add($"Event {id}: previous event in the chain is missing (deleted).");

        var unsealed = await db.AuditEvents.LongCountAsync(e => e.Hash == null, ct);
        return new AuditVerificationResult(problems.Count == 0, checkedCount, unsealed, problems);
    }
}

/// <summary>Seals new audit events into the hash chain in Id order.</summary>
public sealed class AuditSealJob : IScheduledJob
{
    public string Name => "audit-seal";
    public TimeSpan Interval => TimeSpan.FromSeconds(30);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        // Leave very recent rows alone so a transaction that took an earlier identity value can commit first.
        var cutoff = now.AddSeconds(-20);
        var lastHash = await db.AuditEvents.Where(e => e.Hash != null).OrderByDescending(e => e.Id)
            .Select(e => e.Hash).FirstOrDefaultAsync(ct) ?? AuditChain.Genesis;

        var rows = await db.AuditEvents.Where(e => e.Hash == null && e.OccurredAtUtc < cutoff)
            .OrderBy(e => e.Id).Take(1000).ToListAsync(ct);
        foreach (var row in rows)
        {
            row.PreviousHash = lastHash;
            row.Hash = AuditChain.ComputeHash(row, lastHash);
            lastHash = row.Hash;
        }
        if (rows.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
