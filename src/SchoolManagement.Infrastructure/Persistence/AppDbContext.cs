using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using SchoolManagement.Domain.Admission;
using SchoolManagement.Domain.Approvals;
using SchoolManagement.Domain.Audit;
using SchoolManagement.Domain.Common;
using SchoolManagement.Domain.Core;
using SchoolManagement.Domain.Identity;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Domain.Students;
using SchoolManagement.Infrastructure.Common;
using SchoolManagement.Infrastructure.Security;

namespace SchoolManagement.Infrastructure.Persistence;

/// <summary>
/// Single context for the modular monolith. Provider-specific subclasses
/// (<see cref="SqlServerAppDbContext"/>, <see cref="PostgresAppDbContext"/>) carry their own migrations.
/// </summary>
public abstract class AppDbContext : DbContext
{
    private static readonly HashSet<string> MaskedProperties = new(StringComparer.Ordinal)
    {
        nameof(User.PasswordHash), nameof(RefreshToken.TokenHash), nameof(User.SecurityStamp), nameof(Offer.ResponseToken)
    };

    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private int _elevation;

    protected AppDbContext(DbContextOptions options, ICurrentUser currentUser, TimeProvider clock) : base(options)
    {
        _currentUser = currentUser;
        _clock = clock;
        FilterCampusIds = currentUser.CampusIds.ToList();
    }

    public ICurrentUser CurrentUser => _currentUser;

    // Read by the global query filters; EF parameterises these per context instance.
    public bool BypassCampusFilter => _elevation > 0 || _currentUser.IsSystem || _currentUser.HasAllCampusAccess;
    public List<Guid> FilterCampusIds { get; }

    /// <summary>Allows writes to records of a locked/archived academic year (used only by archival tooling).</summary>
    public bool AllowLockedYearWrites { get; set; }

    /// <summary>
    /// Runs the following work with campus filters and campus write checks disabled. Used by public
    /// endpoints (website enquiry form, offer response link) that act on behalf of an anonymous caller.
    /// </summary>
    public IDisposable ElevateToSystem()
    {
        _elevation++;
        return new Elevation(this);
    }

    private sealed class Elevation(AppDbContext db) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            db._elevation--;
        }
    }

    // CORE
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<Campus> Campuses => Set<Campus>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Programme> Programmes => Set<Programme>();
    public DbSet<YearGroup> YearGroups => Set<YearGroup>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<House> Houses => Set<House>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<GradingScheme> GradingSchemes => Set<GradingScheme>();
    public DbSet<GradeBand> GradeBands => Set<GradeBand>();
    public DbSet<FeeHead> FeeHeads => Set<FeeHead>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<ApprovalWorkflow> ApprovalWorkflows => Set<ApprovalWorkflow>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();

    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<OutboundMessage> OutboundMessages => Set<OutboundMessage>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    // ADM
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<EnquiryActivity> EnquiryActivities => Set<EnquiryActivity>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationGuardian> ApplicationGuardians => Set<ApplicationGuardian>();
    public DbSet<ApplicationDocument> ApplicationDocuments => Set<ApplicationDocument>();
    public DbSet<SeatCapacity> SeatCapacities => Set<SeatCapacity>();
    public DbSet<TestSession> TestSessions => Set<TestSession>();
    public DbSet<TestCandidate> TestCandidates => Set<TestCandidate>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<AdmissionInvoice> AdmissionInvoices => Set<AdmissionInvoice>();
    public DbSet<AdmissionInvoiceLine> AdmissionInvoiceLines => Set<AdmissionInvoiceLine>();

    // SIS
    public DbSet<Family> Families => Set<Family>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<StudentEnrolment> StudentEnrolments => Set<StudentEnrolment>();
    public DbSet<StudentDocument> StudentDocuments => Set<StudentDocument>();
    public DbSet<IdCard> IdCards => Set<IdCard>();
    public DbSet<WithdrawalRequest> WithdrawalRequests => Set<WithdrawalRequest>();
    public DbSet<TransferCertificate> TransferCertificates => Set<TransferCertificate>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Money and marks are fixed-precision, never floating point (spec 5.4).
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ModelConfiguration.Configure(modelBuilder);
        ApplyCampusForeignKeys(modelBuilder);
        ApplyGlobalFilters(modelBuilder);
        ModelConfiguration.RestrictCascades(modelBuilder);
    }

    private static void ApplyCampusForeignKeys(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null).ToList())
        {
            if (!typeof(ICampusScoped).IsAssignableFrom(type.ClrType)) continue;
            modelBuilder.Entity(type.ClrType)
                .HasOne(typeof(Campus))
                .WithMany()
                .HasForeignKey(nameof(ICampusScoped.CampusId))
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity(type.ClrType).HasIndex(nameof(ICampusScoped.CampusId));
        }
    }

    /// <summary>Soft-delete and campus isolation filters (CORE-01) applied to every query.</summary>
    private void ApplyGlobalFilters(ModelBuilder modelBuilder)
    {
        var context = Expression.Constant(this);
        var containsMethod = typeof(List<Guid>).GetMethod(nameof(List<Guid>.Contains))!;

        foreach (var type in modelBuilder.Model.GetEntityTypes().Where(t => t.BaseType is null).ToList())
        {
            var clr = type.ClrType;
            var e = Expression.Parameter(clr, "e");
            Expression? body = null;

            if (typeof(ISoftDelete).IsAssignableFrom(clr))
                body = Expression.Not(Expression.Property(e, nameof(ISoftDelete.IsDeleted)));

            if (typeof(ICampusScoped).IsAssignableFrom(clr))
            {
                var campusCheck = Expression.OrElse(
                    Expression.Property(context, nameof(BypassCampusFilter)),
                    Expression.Call(Expression.Property(context, nameof(FilterCampusIds)), containsMethod,
                        Expression.Property(e, nameof(ICampusScoped.CampusId))));
                body = body is null ? campusCheck : Expression.AndAlso(body, campusCheck);
            }

            if (body is not null)
                modelBuilder.Entity(clr).HasQueryFilter(Expression.Lambda(body, e));
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new InvalidOperationException("Use SaveChangesAsync so audit and academic-year checks can run.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();

        // A new child added to a tracked parent's collection already has its (client-generated) key, so EF's
        // change detection assumes it exists and marks it Modified. Anything never loaded or saved is an insert.
        foreach (var entry in ChangeTracker.Entries<Entity>().Where(e => e.State == EntityState.Modified && e.Entity.IsTransient()).ToList())
            entry.State = EntityState.Added;

        var now = _clock.GetUtcNow().UtcDateTime;
        var userId = _currentUser.UserId;
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var softDeleted = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
        {
            if (entry.Entity is ISoftDelete sd && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                sd.IsDeleted = true;
                sd.DeletedAtUtc = now;
                sd.DeletedBy = userId;
                softDeleted.Add(entry.Entity);
            }

            if (entry.Entity is AuditableEntity a)
            {
                if (entry.State == EntityState.Added)
                {
                    a.CreatedAtUtc = now;
                    a.CreatedBy ??= userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    a.UpdatedAtUtc = now;
                    a.UpdatedBy = userId;
                    entry.Property(nameof(AuditableEntity.CreatedAtUtc)).IsModified = false;
                    entry.Property(nameof(AuditableEntity.CreatedBy)).IsModified = false;
                }
            }
        }

        EnforceCampusWriteScope(entries);
        await EnforceAcademicYearLocksAsync(entries, cancellationToken);

        var auditEvents = BuildAuditEvents(entries, softDeleted, now);
        if (auditEvents.Count > 0)
            AuditEvents.AddRange(auditEvents);

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var entry in entries)
            if (entry.Entity is Entity e) e.MarkPersisted();
        return result;
    }

    private void EnforceCampusWriteScope(List<EntityEntry> entries)
    {
        if (BypassCampusFilter) return;
        foreach (var entry in entries)
        {
            if (entry.Entity is not ICampusScoped scoped) continue;
            if (!FilterCampusIds.Contains(scoped.CampusId))
                throw new ForbiddenException("You cannot change records that belong to another campus.");
            if (entry.State == EntityState.Modified &&
                entry.OriginalValues[nameof(ICampusScoped.CampusId)] is Guid original &&
                original != scoped.CampusId && !FilterCampusIds.Contains(original))
                throw new ForbiddenException("You cannot move records out of another campus.");
        }
    }

    private async Task EnforceAcademicYearLocksAsync(List<EntityEntry> entries, CancellationToken ct)
    {
        if (AllowLockedYearWrites) return;
        var yearIds = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (entry.Entity is not IAcademicYearScoped scoped) continue;
            yearIds.Add(scoped.AcademicYearId);
            if (entry.State != EntityState.Added &&
                entry.OriginalValues[nameof(IAcademicYearScoped.AcademicYearId)] is Guid original)
                yearIds.Add(original);
        }
        if (yearIds.Count == 0) return;

        var locked = await AcademicYears.IgnoreQueryFilters().AsNoTracking()
            .Where(y => yearIds.Contains(y.Id) && (y.Status == AcademicYearStatus.Locked || y.Status == AcademicYearStatus.Archived))
            .Select(y => y.Name)
            .ToListAsync(ct);
        if (locked.Count > 0)
            throw new DomainException($"Academic year {string.Join(", ", locked)} is locked; its records are read-only.", "year_locked", isConflict: true);
    }

    private List<AuditEvent> BuildAuditEvents(List<EntityEntry> entries, HashSet<object> softDeleted, DateTime now)
    {
        var events = new List<AuditEvent>();
        foreach (var entry in entries)
        {
            var type = entry.Entity.GetType();
            if (type.GetCustomAttribute<AuditedAttribute>() is null) continue;

            Dictionary<string, object?>? oldValues = null;
            Dictionary<string, object?>? newValues = null;
            AuditAction action;

            if (entry.State == EntityState.Added)
            {
                action = AuditAction.Create;
                newValues = entry.Properties.ToDictionary(p => p.Metadata.Name, p => Mask(p.Metadata.Name, p.CurrentValue));
            }
            else if (entry.State == EntityState.Deleted || softDeleted.Contains(entry.Entity))
            {
                action = AuditAction.Delete;
                oldValues = entry.Properties.ToDictionary(p => p.Metadata.Name, p => Mask(p.Metadata.Name, p.OriginalValue));
            }
            else
            {
                var changed = entry.Properties
                    .Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))
                    .Where(p => p.Metadata.Name is not (nameof(AuditableEntity.UpdatedAtUtc) or nameof(AuditableEntity.UpdatedBy)))
                    .ToList();
                if (changed.Count == 0) continue;
                action = AuditAction.Update;
                oldValues = changed.ToDictionary(p => p.Metadata.Name, p => Mask(p.Metadata.Name, p.OriginalValue));
                newValues = changed.ToDictionary(p => p.Metadata.Name, p => Mask(p.Metadata.Name, p.CurrentValue));
            }

            events.Add(new AuditEvent
            {
                OccurredAtUtc = now,
                UserId = _currentUser.UserId,
                UserName = _currentUser.UserName,
                IpAddress = _currentUser.IpAddress,
                CampusId = (entry.Entity as ICampusScoped)?.CampusId,
                Action = action,
                EntityType = type.Name,
                EntityId = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString(),
                OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
                NewValues = newValues is null ? null : JsonSerializer.Serialize(newValues)
            });
        }
        return events;
    }

    private static object? Mask(string property, object? value) =>
        MaskedProperties.Contains(property) && value is not null ? "***" : value;
}

public sealed class SqlServerAppDbContext(DbContextOptions<SqlServerAppDbContext> options, ICurrentUser currentUser, TimeProvider clock)
    : AppDbContext(options, currentUser, clock);

public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options, ICurrentUser currentUser, TimeProvider clock)
    : AppDbContext(options, currentUser, clock);
