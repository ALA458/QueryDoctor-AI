using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>
/// Contexto de EF Core sobre PostgreSQL. S1-04 cubre los esquemas core y ops;
/// S1-13 agrega las tablas del motor en el esquema analysis; health_score (S2-02), ai (S1-17) y audit (S2-06) llegan en sus propias tareas.
/// </summary>
public sealed class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Connection> Connections => Set<Connection>();

    public DbSet<CapabilityProfile> CapabilityProfiles => Set<CapabilityProfile>();

    public DbSet<QueryText> QueryTexts => Set<QueryText>();

    public DbSet<Query> Queries => Set<Query>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<RuntimeStat> RuntimeStats => Set<RuntimeStat>();

    public DbSet<IngestionRun> IngestionRuns => Set<IngestionRun>();

    public DbSet<UserConnection> UserConnections => Set<UserConnection>();

    public DbSet<RuleCatalogEntry> RuleCatalog => Set<RuleCatalogEntry>();

    public DbSet<ThresholdSet> ThresholdSets => Set<ThresholdSet>();

    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();

    public DbSet<Finding> Findings => Set<Finding>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Las tablas de Identity viven en el esquema ops, con nombres en snake_case.
        builder.Entity<ApplicationUser>().ToTable("users", "ops");
        builder.Entity<ApplicationRole>().ToTable("roles", "ops");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", "ops");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", "ops");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", "ops");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", "ops");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", "ops");

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(SnakeCase.Convert(property.Name));
            }
        }
    }
}
