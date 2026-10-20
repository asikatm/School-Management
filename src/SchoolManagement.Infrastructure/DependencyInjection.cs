using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolManagement.Domain.Notifications;
using SchoolManagement.Infrastructure.Approvals;
using SchoolManagement.Infrastructure.Auditing;
using SchoolManagement.Infrastructure.Backup;
using SchoolManagement.Infrastructure.Documents;
using SchoolManagement.Infrastructure.Jobs;
using SchoolManagement.Infrastructure.Notifications;
using SchoolManagement.Infrastructure.Numbering;
using SchoolManagement.Infrastructure.Persistence;
using SchoolManagement.Infrastructure.Security;
using SchoolManagement.Infrastructure.Settings;
using SchoolManagement.Infrastructure.Storage;

namespace SchoolManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddSingleton(TimeProvider.System);

        var dbOptions = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        if (dbOptions.Provider == DatabaseProvider.PostgreSql)
        {
            services.AddDbContext<AppDbContext, PostgresAppDbContext>(o => o
                .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory"))
                .AddInterceptors(PersistedEntityMarker.Instance)
                .ConfigureWarnings(IgnoreFilterWarning));
        }
        else
        {
            services.AddDbContext<AppDbContext, SqlServerAppDbContext>(o => o
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3))
                .AddInterceptors(PersistedEntityMarker.Instance)
                .ConfigureWarnings(IgnoreFilterWarning));
        }

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<INumberSequenceService, NumberSequenceService>();
        services.AddSingleton<IQrCodeService, QrCodeService>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditVerifier, AuditVerifier>();
        services.AddScoped<IApprovalService, ApprovalService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IBackupService, BackupService>();
        services.AddScoped<DataSeeder>();
        services.AddScoped<ISettingsService, SettingsService>();

        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.PostConfigure<StorageOptions>(o => o.RootPath = Path.GetFullPath(Path.Combine(contentRoot, o.RootPath)));
        services.AddSingleton<IFileScanner, DefenderFileScanner>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        services.Configure<SmsOptions>(configuration.GetSection("Notifications:Sms"));
        services.Configure<SmtpOptions>(configuration.GetSection("Notifications:Smtp"));
        services.Configure<BackupOptions>(configuration.GetSection("Backup"));
        services.AddHttpClient<HttpSmsSender>();
        services.AddScoped<IMessageSender>(sp => sp.GetRequiredService<HttpSmsSender>());
        services.AddScoped<IMessageSender, SmtpEmailSender>();
        services.AddScoped<IMessageSender, InAppSender>();
        services.AddScoped<IMessageSender>(sp => new LoggingSender(NotificationChannel.Push, sp.GetRequiredService<ILogger<LoggingSender>>()));
        services.AddScoped<IMessageSender>(sp => new LoggingSender(NotificationChannel.WhatsApp, sp.GetRequiredService<ILogger<LoggingSender>>()));

        services.AddSingleton<IScheduledJob, NotificationDispatchJob>();
        services.AddSingleton<IScheduledJob, AuditSealJob>();
        services.AddSingleton<IScheduledJob, ApprovalEscalationJob>();
        services.AddSingleton<IScheduledJob, DailyBackupJob>();
        services.AddHostedService<ScheduledJobRunner>();

        return services;
    }

    // Campus and soft-delete filters on principals are intentional; dependents are filtered the same way.
    private static void IgnoreFilterWarning(WarningsConfigurationBuilder w) =>
        w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning);
}
