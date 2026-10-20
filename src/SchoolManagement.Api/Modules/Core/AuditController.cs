using System.Text;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Infrastructure.Auditing;

namespace SchoolManagement.Api.Modules.Core;

/// <summary>CORE-05: searchable, exportable, tamper-evident audit trail.</summary>
[ApiController]
[Route("api/audit")]
[Tags("Core: Audit")]
public sealed class AuditController(AppDbContext db, IAuditVerifier verifier, IAuditLog audit) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Audit.View)]
    public async Task<PagedResult<AuditEventView>> Search([FromQuery] AuditQuery query, [FromQuery] PageQuery page, CancellationToken ct) =>
        await Filter(query).OrderByDescending(e => e.Id).Select(e => ToView(e)).ToPagedAsync(page, ct);

    /// <summary>Recomputes the hash chain and reports any modified or deleted events.</summary>
    [HttpGet("verify")]
    [HasPermission(Permissions.Audit.View)]
    public Task<AuditVerificationResult> Verify(CancellationToken ct) => verifier.VerifyAsync(ct);

    /// <summary>CSV export (max 50,000 rows). The export itself is audited.</summary>
    [HttpGet("export")]
    [HasPermission(Permissions.Audit.Export)]
    public async Task<FileContentResult> Export([FromQuery] AuditQuery query, CancellationToken ct)
    {
        var rows = await Filter(query).OrderBy(e => e.Id).Take(50_000).ToListAsync(ct);
        var sb = new StringBuilder("Id,OccurredAtUtc,User,Ip,Campus,Action,EntityType,EntityId,Description,OldValues,NewValues,Hash\n");
        foreach (var e in rows)
            sb.AppendJoin(',', e.Id, e.OccurredAtUtc.ToString("O"), Csv(e.UserName), Csv(e.IpAddress), e.CampusId, e.Action, Csv(e.EntityType),
                Csv(e.EntityId), Csv(e.Description), Csv(e.OldValues), Csv(e.NewValues), e.Hash).Append('\n');

        audit.Add(AuditAction.Export, nameof(AuditEvent), null, $"Exported {rows.Count} audit events");
        await db.SaveChangesAsync(ct);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"audit_{DateTime.UtcNow:yyyyMMddHHmm}.csv");
    }

    private IQueryable<AuditEvent> Filter(AuditQuery q)
    {
        var events = db.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.EntityType)) events = events.Where(e => e.EntityType == q.EntityType);
        if (!string.IsNullOrWhiteSpace(q.EntityId)) events = events.Where(e => e.EntityId == q.EntityId);
        if (q.UserId is not null) events = events.Where(e => e.UserId == q.UserId);
        if (q.CampusId is not null) events = events.Where(e => e.CampusId == q.CampusId);
        if (q.Action is not null) events = events.Where(e => e.Action == q.Action);
        if (q.FromUtc is not null) events = events.Where(e => e.OccurredAtUtc >= q.FromUtc);
        if (q.ToUtc is not null) events = events.Where(e => e.OccurredAtUtc <= q.ToUtc);
        if (!string.IsNullOrWhiteSpace(q.Text))
            events = events.Where(e => (e.Description != null && e.Description.Contains(q.Text)) || (e.NewValues != null && e.NewValues.Contains(q.Text)) || (e.OldValues != null && e.OldValues.Contains(q.Text)));
        return events;
    }

    private static string Csv(string? value) =>
        value is null ? "" : "\"" + value.Replace("\"", "\"\"") + "\"";

    private static AuditEventView ToView(AuditEvent e) => new(e.Id, e.OccurredAtUtc, e.UserId, e.UserName, e.IpAddress, e.CampusId, e.Action,
        e.EntityType, e.EntityId, e.Description, e.OldValues, e.NewValues, e.Hash is not null);
}

public record AuditQuery(string? EntityType, string? EntityId, Guid? UserId, Guid? CampusId, AuditAction? Action, DateTime? FromUtc, DateTime? ToUtc, string? Text);
public record AuditEventView(long Id, DateTime OccurredAtUtc, Guid? UserId, string? UserName, string? IpAddress, Guid? CampusId, AuditAction Action,
    string EntityType, string? EntityId, string? Description, string? OldValues, string? NewValues, bool IsSealed);
