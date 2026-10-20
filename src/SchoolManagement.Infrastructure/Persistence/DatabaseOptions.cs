using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SchoolManagement.Infrastructure.Security;

namespace SchoolManagement.Infrastructure.Persistence;

public enum DatabaseProvider
{
    SqlServer = 0,
    PostgreSql = 1
}

public class DatabaseOptions
{
    /// <summary>PostgreSQL is the recommended production database (spec 5.3); SQL Server is the supported alternative.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;
    public bool MigrateOnStartup { get; set; } = true;
    public bool SeedOnStartup { get; set; } = true;
}

// Design-time factories used only by `dotnet ef migrations add`; each provider keeps its own migrations folder.

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerAppDbContext>
{
    public SqlServerAppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqlServerAppDbContext>()
            .UseSqlServer("Server=localhost;Database=SchoolManagement;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        return new SqlServerAppDbContext(options, SystemUser.Instance, TimeProvider.System);
    }
}

public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql("Host=localhost;Database=school_management;Username=postgres;Password=postgres")
            .Options;
        return new PostgresAppDbContext(options, SystemUser.Instance, TimeProvider.System);
    }
}
