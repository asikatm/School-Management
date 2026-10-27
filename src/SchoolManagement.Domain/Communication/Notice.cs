using SchoolManagement.Domain.Common;

namespace SchoolManagement.Domain.Communication;

/// <summary>
/// A notice or circular on the notice board (Appendix C "notice or circular published"). Targeted by audience,
/// campus (null = every campus) and class (null = every class); urgent notices also go to guardians by SMS.
/// </summary>
[Audited]
public class Notice : SoftDeleteEntity
{
    public Guid? CampusId { get; set; }
    public Guid? YearGroupId { get; set; }
    public required string Title { get; set; }
    public string? TitleBn { get; set; }
    public required string Body { get; set; }
    public bool ForStudents { get; set; } = true;
    public bool ForParents { get; set; } = true;
    public bool ForStaff { get; set; }
    public bool IsPinned { get; set; }
    public bool IsUrgent { get; set; }
    public DateTime PublishedAtUtc { get; set; }

    /// <summary>Hidden from the board after this day.</summary>
    public DateOnly? ExpiresOn { get; set; }
    public string? AttachmentPath { get; set; }
    public string? AttachmentName { get; set; }
    public string? AttachmentType { get; set; }
}
