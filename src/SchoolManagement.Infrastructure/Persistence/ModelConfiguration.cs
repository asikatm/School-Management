using Microsoft.EntityFrameworkCore;
using SchoolManagement.Domain.Academics;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Approvals;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Identity;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Domain.Students;

namespace SchoolManagement.Infrastructure.Persistence;

internal static class ModelConfiguration
{
    private const int Code = 32;
    private const int Name = 200;
    private const int Phone = 32;
    private const int Email = 256;

    /// <summary>Dependents whose rows only make sense inside their parent; everything else is Restrict.</summary>
    private static readonly HashSet<Type> CascadeDependents =
    [
        typeof(GradeBand), typeof(RolePermission), typeof(ApprovalStep), typeof(ApprovalDecision),
        typeof(ApplicationGuardian), typeof(AdmissionInvoiceLine), typeof(EnquiryActivity), typeof(TestCandidate),
        typeof(Term), typeof(UserRoleAssignment)
    ];

    public static void Configure(ModelBuilder b)
    {
        ConfigureCore(b);
        ConfigureIdentity(b);
        ConfigureAuditAndApprovals(b);
        ConfigureNotifications(b);
        ConfigureAdmission(b);
        ConfigureStudents(b);
        ConfigureAcademics(b);
    }

    private static void ConfigureAcademics(ModelBuilder b)
    {
        b.Entity<Room>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Code);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.CampusId, x.Code }).IsUnique();
        });

        b.Entity<SubjectGroup>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Name);
            e.HasOne<YearGroup>().WithMany().HasForeignKey(x => x.YearGroupId);
        });

        b.Entity<CurriculumSubject>(e =>
        {
            e.HasIndex(x => new { x.YearGroupId, x.SubjectId }).IsUnique();
            e.HasOne<YearGroup>().WithMany().HasForeignKey(x => x.YearGroupId);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId);
            e.HasOne<SubjectGroup>().WithMany().HasForeignKey(x => x.SubjectGroupId);
        });

        b.Entity<StudentSubject>(e =>
        {
            e.HasIndex(x => new { x.StudentId, x.AcademicYearId, x.SubjectId }).IsUnique();
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
        });

        b.Entity<TeacherAllocation>(e =>
        {
            e.HasIndex(x => new { x.SectionId, x.SubjectId, x.TeacherUserId }).IsUnique();
            e.HasIndex(x => new { x.AcademicYearId, x.TeacherUserId });
            e.HasOne<Section>().WithMany().HasForeignKey(x => x.SectionId);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.TeacherUserId);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
        });

        b.Entity<PeriodDefinition>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64);
            e.HasIndex(x => new { x.CampusId, x.ShiftId, x.PeriodNo }).IsUnique();
            e.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId);
        });

        b.Entity<TimetableSlot>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(200);
            // Several slots may share a cell only as an option block (parallel electives of one subject group).
            e.HasIndex(x => new { x.SectionId, x.Day, x.PeriodNo, x.SubjectId }).IsUnique();
            e.HasIndex(x => new { x.AcademicYearId, x.TeacherUserId, x.Day });
            e.HasIndex(x => new { x.AcademicYearId, x.RoomId, x.Day });
            e.HasOne<Section>().WithMany().HasForeignKey(x => x.SectionId);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.TeacherUserId);
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
        });

        b.Entity<Substitution>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.Date, x.SubstituteUserId });
            e.HasIndex(x => new { x.TimetableSlotId, x.Date });
            e.HasOne<TimetableSlot>().WithMany().HasForeignKey(x => x.TimetableSlotId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.SubstituteUserId);
        });

        b.Entity<CalendarEvent>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(Name);
            e.Property(x => x.TitleBn).HasMaxLength(Name);
            e.Property(x => x.Location).HasMaxLength(Name);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasIndex(x => new { x.StartDate, x.EndDate });
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
            e.HasOne<Campus>().WithMany().HasForeignKey(x => x.CampusId);
        });
    }

    public static void RestrictCascades(ModelBuilder b)
    {
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            if (fk.IsOwnership) continue;
            fk.DeleteBehavior = CascadeDependents.Contains(fk.DeclaringEntityType.ClrType) && fk.IsRequired
                ? DeleteBehavior.Cascade
                : DeleteBehavior.Restrict;
        }
    }

    private static void ConfigureCore(ModelBuilder b)
    {
        b.Entity<Organisation>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.Property(x => x.Code).HasMaxLength(Code);
            e.Property(x => x.TimeZone).HasMaxLength(64);
        });

        b.Entity<Campus>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Code);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.Property(x => x.Phone).HasMaxLength(Phone);
            e.Property(x => x.Email).HasMaxLength(Email);
            e.HasIndex(x => new { x.OrganisationId, x.Code }).IsUnique();
            e.HasOne(x => x.Organisation).WithMany(o => o.Campuses).HasForeignKey(x => x.OrganisationId);
        });

        b.Entity<AcademicYear>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64);
            e.HasIndex(x => new { x.OrganisationId, x.Name }).IsUnique();
            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId);
            e.HasMany(x => x.Terms).WithOne(t => t.AcademicYear).HasForeignKey(t => t.AcademicYearId);
            e.Ignore(x => x.IsWritable);
        });

        b.Entity<Term>(e => e.Property(x => x.Name).HasMaxLength(64));

        b.Entity<Programme>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Code);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.HasIndex(x => new { x.OrganisationId, x.Code }).IsUnique();
            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId);
            e.HasOne(x => x.DefaultGradingScheme).WithMany().HasForeignKey(x => x.DefaultGradingSchemeId);
            e.HasMany(x => x.YearGroups).WithOne(y => y.Programme).HasForeignKey(y => y.ProgrammeId);
        });

        b.Entity<YearGroup>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Code);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<YearGroup>().WithMany().HasForeignKey(x => x.NextYearGroupId);
        });

        b.Entity<Shift>(e => e.Property(x => x.Name).HasMaxLength(64));

        b.Entity<Section>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64);
            e.Property(x => x.Room).HasMaxLength(64);
            e.HasIndex(x => new { x.CampusId, x.AcademicYearId, x.YearGroupId, x.Name }).IsUnique();
            e.HasOne(x => x.YearGroup).WithMany().HasForeignKey(x => x.YearGroupId);
            e.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ClassTeacherUserId);
        });

        b.Entity<Subject>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Code);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.Property(x => x.ExternalCode).HasMaxLength(Code);
            e.HasIndex(x => new { x.OrganisationId, x.Code }).IsUnique();
        });

        b.Entity<House>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.Property(x => x.Colour).HasMaxLength(16);
        });

        b.Entity<Holiday>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
            e.HasOne<Campus>().WithMany().HasForeignKey(x => x.CampusId);
        });

        b.Entity<GradingScheme>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Code);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.HasIndex(x => new { x.OrganisationId, x.Code }).IsUnique();
            e.HasMany(x => x.Bands).WithOne().HasForeignKey(x => x.GradingSchemeId);
        });

        b.Entity<GradeBand>(e =>
        {
            e.Property(x => x.Grade).HasMaxLength(16);
            e.Property(x => x.Descriptor).HasMaxLength(64);
            e.Property(x => x.DescriptorBn).HasMaxLength(64);
            e.Property(x => x.MinPercent).HasPrecision(5, 2);
            e.Property(x => x.MaxPercent).HasPrecision(5, 2);
            e.Property(x => x.GradePoint).HasPrecision(4, 2);
        });

        b.Entity<FeeHead>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(Code);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.HasIndex(x => new { x.OrganisationId, x.Code }).IsUnique();
        });

        b.Entity<NumberSequence>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Prefix).HasMaxLength(Code);
            e.Property(x => x.Pattern).HasMaxLength(128);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.Key, x.CampusId, x.AcademicYearId }).IsUnique();
        });

        b.Entity<SystemSetting>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(128);
            e.HasIndex(x => new { x.Key, x.CampusId }).IsUnique();
        });

        b.Entity<BackupRecord>(e =>
        {
            e.Property(x => x.Trigger).HasMaxLength(32);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.StartedAtUtc);
        });
    }

    private static void ConfigureIdentity(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(128);
            e.Property(x => x.FullName).HasMaxLength(Name);
            e.Property(x => x.Email).HasMaxLength(Email);
            e.Property(x => x.Phone).HasMaxLength(Phone);
            e.Property(x => x.PasswordHash).HasMaxLength(128);
            e.Property(x => x.PreferredLanguage).HasMaxLength(8);
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.Phone);
            e.HasMany(x => x.RoleAssignments).WithOne(a => a.User).HasForeignKey(a => a.UserId);
        });

        b.Entity<Role>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(64);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(p => p.RoleId);
        });

        b.Entity<RolePermission>(e =>
        {
            e.Property(x => x.Permission).HasMaxLength(64);
            e.HasIndex(x => new { x.RoleId, x.Permission }).IsUnique();
        });

        b.Entity<UserRoleAssignment>(e =>
        {
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
            e.HasOne<Campus>().WithMany().HasForeignKey(x => x.CampusId);
            e.HasIndex(x => new { x.UserId, x.RoleId, x.CampusId }).IsUnique();
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(128);
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });
    }

    private static void ConfigureAuditAndApprovals(ModelBuilder b)
    {
        b.Entity<AuditEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.UserName).HasMaxLength(128);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.EntityType).HasMaxLength(128);
            e.Property(x => x.EntityId).HasMaxLength(64);
            e.Property(x => x.PreviousHash).HasMaxLength(64);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.OccurredAtUtc);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<ApprovalWorkflow>(e =>
        {
            e.Property(x => x.RequestType).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.WorkflowId);
            e.HasOne<Campus>().WithMany().HasForeignKey(x => x.CampusId);
        });

        b.Entity<ApprovalStep>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Name);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.ApproverRoleId);
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.EscalateToRoleId);
        });

        b.Entity<ApprovalRequest>(e =>
        {
            e.Property(x => x.RequestType).HasMaxLength(64);
            e.Property(x => x.EntityType).HasMaxLength(128);
            e.Property(x => x.EntityId).HasMaxLength(64);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.HasOne(x => x.Workflow).WithMany().HasForeignKey(x => x.WorkflowId);
            e.HasMany(x => x.Decisions).WithOne().HasForeignKey(d => d.RequestId);
            e.HasIndex(x => new { x.Status, x.RequestType });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        b.Entity<ApprovalDelegation>(e => e.Property(x => x.RequestType).HasMaxLength(64));
    }

    private static void ConfigureNotifications(ModelBuilder b)
    {
        b.Entity<MessageTemplate>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Language).HasMaxLength(8);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.HasIndex(x => new { x.Code, x.Channel, x.Language }).IsUnique();
        });

        b.Entity<OutboundMessage>(e =>
        {
            e.Property(x => x.TemplateCode).HasMaxLength(64);
            e.Property(x => x.Recipient).HasMaxLength(Email);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.ProviderMessageId).HasMaxLength(128);
            e.Property(x => x.RelatedEntityType).HasMaxLength(128);
            e.Property(x => x.RelatedEntityId).HasMaxLength(64);
            e.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            e.HasIndex(x => x.RecipientUserId);
        });

        b.Entity<NotificationPreference>(e =>
        {
            e.Property(x => x.Language).HasMaxLength(8);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.GuardianId);
        });
    }

    private static void ConfigureAdmission(ModelBuilder b)
    {
        b.Entity<Enquiry>(e =>
        {
            e.Property(x => x.EnquiryNo).HasMaxLength(Code);
            e.Property(x => x.CampaignCode).HasMaxLength(64);
            e.Property(x => x.ChildName).HasMaxLength(Name);
            e.Property(x => x.GuardianName).HasMaxLength(Name);
            e.Property(x => x.GuardianPhone).HasMaxLength(Phone);
            e.Property(x => x.GuardianEmail).HasMaxLength(Email);
            e.Property(x => x.LostReason).HasMaxLength(500);
            e.HasIndex(x => x.EnquiryNo).IsUnique();
            e.HasIndex(x => x.GuardianPhone);
            e.HasIndex(x => new { x.Stage, x.Source });
            e.HasMany(x => x.Activities).WithOne().HasForeignKey(a => a.EnquiryId);
            e.HasOne<YearGroup>().WithMany().HasForeignKey(x => x.YearGroupId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedToUserId);
        });

        b.Entity<Application>(e =>
        {
            e.Property(x => x.ApplicationNo).HasMaxLength(Code);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.Property(x => x.Gender).HasMaxLength(16);
            e.Property(x => x.Religion).HasMaxLength(32);
            e.Property(x => x.Nationality).HasMaxLength(64);
            e.Property(x => x.BloodGroup).HasMaxLength(8);
            e.Property(x => x.BirthCertificateNo).HasMaxLength(32);
            e.Property(x => x.PreviousSchool).HasMaxLength(Name);
            e.Property(x => x.PreviousClass).HasMaxLength(64);
            e.Property(x => x.ApplicationFeeReference).HasMaxLength(128);
            e.Property(x => x.AccessTokenHash).HasMaxLength(64);
            e.HasIndex(x => x.AccessTokenHash).IsUnique();
            e.Property(x => x.TestScore).HasPrecision(7, 2);
            e.Property(x => x.InterviewScore).HasPrecision(7, 2);
            e.Property(x => x.MeritScore).HasPrecision(7, 2);
            e.Ignore(x => x.FullName);
            e.HasIndex(x => x.ApplicationNo).IsUnique();
            e.HasIndex(x => x.BirthCertificateNo);
            e.HasIndex(x => new { x.AcademicYearId, x.YearGroupId, x.Status });
            e.HasMany(x => x.Guardians).WithOne().HasForeignKey(g => g.ApplicationId);
            e.HasMany(x => x.Documents).WithOne().HasForeignKey(d => d.ApplicationId);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
            e.HasOne<YearGroup>().WithMany().HasForeignKey(x => x.YearGroupId);
            e.HasOne<Enquiry>().WithMany().HasForeignKey(x => x.EnquiryId);
        });

        b.Entity<ApplicationGuardian>(e =>
        {
            e.Property(x => x.Relationship).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.Phone).HasMaxLength(Phone);
            e.Property(x => x.Email).HasMaxLength(Email);
            e.Property(x => x.NationalId).HasMaxLength(32);
            e.Property(x => x.Occupation).HasMaxLength(100);
            e.Property(x => x.Organisation).HasMaxLength(Name);
            e.Property(x => x.IncomeBand).HasMaxLength(32);
        });

        b.Entity<ApplicationDocument>(e =>
        {
            e.Property(x => x.DocumentType).HasMaxLength(64);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.StoragePath).HasMaxLength(500);
            e.Property(x => x.ContentType).HasMaxLength(100);
        });

        b.Entity<SeatCapacity>(e =>
        {
            e.HasIndex(x => new { x.CampusId, x.AcademicYearId, x.YearGroupId }).IsUnique();
            e.HasOne<YearGroup>().WithMany().HasForeignKey(x => x.YearGroupId);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
        });

        b.Entity<TestSession>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.Room).HasMaxLength(64);
            e.Property(x => x.InvigilatorName).HasMaxLength(Name);
            e.Property(x => x.MaxScore).HasPrecision(7, 2);
            e.HasMany(x => x.Candidates).WithOne().HasForeignKey(c => c.TestSessionId);
            e.HasOne<YearGroup>().WithMany().HasForeignKey(x => x.YearGroupId);
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
        });

        b.Entity<TestCandidate>(e =>
        {
            e.Property(x => x.AdmitCardNo).HasMaxLength(Code);
            e.Property(x => x.SeatNo).HasMaxLength(16);
            e.Property(x => x.Score).HasPrecision(7, 2);
            e.HasIndex(x => x.AdmitCardNo).IsUnique();
            e.HasIndex(x => new { x.TestSessionId, x.ApplicationId }).IsUnique();
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId);
        });

        b.Entity<Interview>(e =>
        {
            e.Property(x => x.Location).HasMaxLength(Name);
            e.Property(x => x.InterviewerName).HasMaxLength(Name);
            e.Property(x => x.Score).HasPrecision(7, 2);
            e.HasOne<Application>().WithMany().HasForeignKey(x => x.ApplicationId);
        });

        b.Entity<WaitlistEntry>(e =>
        {
            e.HasIndex(x => new { x.CampusId, x.AcademicYearId, x.YearGroupId, x.IsActive, x.Position });
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId);
        });

        b.Entity<Offer>(e =>
        {
            e.Property(x => x.OfferNo).HasMaxLength(Code);
            e.Property(x => x.ResponseToken).HasMaxLength(64);
            e.Property(x => x.Language).HasMaxLength(8);
            e.HasIndex(x => x.OfferNo).IsUnique();
            e.HasIndex(x => x.ResponseToken).IsUnique();
            e.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
            e.HasOne(x => x.Application).WithMany().HasForeignKey(x => x.ApplicationId);
        });

        b.Entity<AdmissionInvoice>(e =>
        {
            e.Property(x => x.InvoiceNo).HasMaxLength(Code);
            e.Property(x => x.PaymentChannel).HasMaxLength(32);
            e.Property(x => x.PaymentReference).HasMaxLength(128);
            e.HasIndex(x => x.InvoiceNo).IsUnique();
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.InvoiceId);
            e.HasOne<Application>().WithMany().HasForeignKey(x => x.ApplicationId);
            e.HasOne<Offer>().WithMany().HasForeignKey(x => x.OfferId);
        });

        b.Entity<AdmissionInvoiceLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(Name);
            e.HasOne<FeeHead>().WithMany().HasForeignKey(x => x.FeeHeadId);
        });
    }

    private static void ConfigureStudents(ModelBuilder b)
    {
        b.Entity<Family>(e =>
        {
            e.Property(x => x.FamilyCode).HasMaxLength(Code);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.HasIndex(x => x.FamilyCode).IsUnique();
            e.HasMany(x => x.Students).WithOne(s => s.Family).HasForeignKey(s => s.FamilyId);
        });

        b.Entity<Student>(e =>
        {
            e.Property(x => x.AdmissionNo).HasMaxLength(Code);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.Property(x => x.Gender).HasMaxLength(16);
            e.Property(x => x.BloodGroup).HasMaxLength(8);
            e.Property(x => x.Religion).HasMaxLength(32);
            e.Property(x => x.Nationality).HasMaxLength(64);
            e.Property(x => x.BirthCertificateNo).HasMaxLength(32);
            e.Property(x => x.NationalId).HasMaxLength(32);
            e.Property(x => x.PhotoPath).HasMaxLength(500);
            e.Property(x => x.PreviousSchool).HasMaxLength(Name);
            e.Property(x => x.Languages).HasMaxLength(Name);
            e.Property(x => x.Email).HasMaxLength(Email);
            e.Property(x => x.Phone).HasMaxLength(Phone);
            e.Property(x => x.RfidNo).HasMaxLength(64);
            e.Ignore(x => x.FullName);
            e.HasIndex(x => x.AdmissionNo).IsUnique();
            e.HasIndex(x => x.BirthCertificateNo);
            e.HasIndex(x => x.RfidNo);
            e.HasIndex(x => new { x.FirstName, x.LastName });
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.Enrolments).WithOne(en => en.Student).HasForeignKey(en => en.StudentId);
            e.HasMany(x => x.Guardians).WithOne(g => g.Student).HasForeignKey(g => g.StudentId);
            e.HasMany(x => x.Documents).WithOne().HasForeignKey(d => d.StudentId);
            e.HasOne<House>().WithMany().HasForeignKey(x => x.HouseId);
            e.HasOne<Application>().WithMany().HasForeignKey(x => x.ApplicationId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
            // CurrentEnrolmentId is deliberately not a foreign key: Student and StudentEnrolment are inserted
            // together on enrolment, and a FK in both directions would make that insert order circular.
        });

        b.Entity<Guardian>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.NameBn).HasMaxLength(Name);
            e.Property(x => x.Phone).HasMaxLength(Phone);
            e.Property(x => x.AltPhone).HasMaxLength(Phone);
            e.Property(x => x.Email).HasMaxLength(Email);
            e.Property(x => x.NationalId).HasMaxLength(32);
            e.Property(x => x.Occupation).HasMaxLength(100);
            e.Property(x => x.Organisation).HasMaxLength(Name);
            e.Property(x => x.IncomeBand).HasMaxLength(32);
            e.Property(x => x.PhotoPath).HasMaxLength(500);
            e.HasIndex(x => x.Phone);
            e.HasIndex(x => x.NationalId);
            e.HasMany(x => x.Students).WithOne(sg => sg.Guardian).HasForeignKey(sg => sg.GuardianId);
            e.HasOne<Family>().WithMany().HasForeignKey(x => x.FamilyId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<StudentGuardian>(e =>
        {
            e.Property(x => x.Relationship).HasMaxLength(32);
            e.HasIndex(x => new { x.StudentId, x.GuardianId }).IsUnique();
        });

        b.Entity<StudentEnrolment>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.AcademicYearId, x.SectionId, x.RollNo });
            e.HasIndex(x => new { x.StudentId, x.Status });
            e.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.AcademicYearId);
            e.HasOne<YearGroup>().WithMany().HasForeignKey(x => x.YearGroupId);
            e.HasOne<Section>().WithMany().HasForeignKey(x => x.SectionId);
        });

        b.Entity<StudentDocument>(e =>
        {
            e.Property(x => x.DocumentType).HasMaxLength(64);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.StoragePath).HasMaxLength(500);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Sha256).HasMaxLength(64);
        });

        b.Entity<IdCard>(e =>
        {
            e.Property(x => x.CardNo).HasMaxLength(Code);
            e.Property(x => x.ReissueReason).HasMaxLength(200);
            e.HasIndex(x => x.CardNo).IsUnique();
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
        });

        b.Entity<WithdrawalRequest>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DestinationSchool).HasMaxLength(Name);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId);
        });

        b.Entity<TransferCertificate>(e =>
        {
            e.Property(x => x.CertificateNo).HasMaxLength(Code);
            e.Property(x => x.StudentName).HasMaxLength(Name);
            e.Property(x => x.LastClass).HasMaxLength(100);
            e.Property(x => x.Conduct).HasMaxLength(100);
            e.Property(x => x.VerificationCode).HasMaxLength(16);
            e.HasIndex(x => x.CertificateNo).IsUnique();
            e.HasIndex(x => x.VerificationCode).IsUnique();
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId);
            e.HasOne<WithdrawalRequest>().WithMany().HasForeignKey(x => x.WithdrawalRequestId);
        });
    }
}
