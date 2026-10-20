using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Core;

public enum BackupStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>CORE-13: history of automated and manual database backups.</summary>
public class BackupRecord : Entity
{
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public BackupStatus Status { get; set; }
    public required string Trigger { get; set; }
    public string? FilePath { get; set; }
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? Error { get; set; }
}
