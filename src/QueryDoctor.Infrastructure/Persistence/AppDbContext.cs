using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>
/// Contexto de EF Core sobre PostgreSQL. S1-04 cubre los esquemas core y ops;
/// S1-13 agrega las tablas del motor en el esquema analysis; health_score (S2-02), ai (S1-17) y audit (S2-06) llegan en sus propias tareas.
/// </summary>
public sealed class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    /// <summary>Nombre del filtro global por conexión (S2-12, RNF-SEG-12).</summary>
    public const string ConnectionAccessFilter = "ConnectionAccess";

    private readonly IAccessScope _accessScope;

    /// <summary>
    /// El contexto de acceso es obligatorio: un host que no lo registre no puede crear el contexto (S2-12). La Api usa el
    /// del usuario de la solicitud; el Worker, las migraciones y las pruebas declaran <see cref="SystemAccessScope"/>.
    /// </summary>
    public AppDbContext(DbContextOptions<AppDbContext> options, IAccessScope accessScope)
        : base(options)
    {
        _accessScope = accessScope ?? throw new ArgumentNullException(nameof(accessScope));
    }

    // Los filtros leen estas propiedades en cada consulta: EF las parametriza por instancia del contexto.
    private bool AccessUnrestricted => _accessScope.Kind is AccessKind.Dba or AccessKind.System;

    private Guid[] AllowedConnectionIds =>
        _accessScope.Kind == AccessKind.Developer ? [.. _accessScope.AssignedConnectionIds] : [];

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

    public DbSet<HealthScore> HealthScores => Set<HealthScore>();

    public DbSet<KbFragment> KbFragments => Set<KbFragment>();

    public DbSet<KbCausalRelation> KbCausalRelations => Set<KbCausalRelation>();

    /// <summary>Explicaciones generadas o reutilizadas de caché (S2-05, RF-IA-06/07).</summary>
    public DbSet<Explanation> Explanations => Set<Explanation>();

    /// <summary>Afirmaciones tipadas de cada explicación (§7, RF-IA-02).</summary>
    public DbSet<ExplanationClaim> ExplanationClaims => Set<ExplanationClaim>();

    /// <summary>Referencias de cada afirmación a hallazgos y nodos del plan (§7, RF-DASH-05).</summary>
    public DbSet<ExplanationClaimReference> ExplanationClaimReferences => Set<ExplanationClaimReference>();

    /// <summary>Registro de trazabilidad de solo adición, encadenado (S2-06, RF-TRZ-01/02).</summary>
    public DbSet<TraceEntry> TraceEntries => Set<TraceEntry>();

    /// <summary>Log de auditoría de solo adición, encadenado (S2-06, RF-TRZ-05).</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

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

        ApplyConnectionAccessFilters(builder);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(SnakeCase.Convert(property.Name));
            }
        }
    }

    /// <summary>
    /// Filtro por conexión asignada en toda entidad con datos de una conexión (S2-12, RNF-SEG-12). Sin
    /// <c>IgnoreQueryFilters</c> en ningún lado: el contexto Sistema ya ve todo. Quedan fuera, a propósito,
    /// <c>ops.user_connection</c> (es la fuente del propio scope) y <c>audit.*</c> (ChainWriter lee la última seq de toda
    /// la cadena, también desde la Api; solo el DBA la consulta). Lo vigila <c>ConnectionAccessFilterCoverageTests</c>.
    /// </summary>
    private void ApplyConnectionAccessFilters(ModelBuilder builder)
    {
        builder.Entity<Connection>().HasQueryFilter(
            ConnectionAccessFilter, c => AccessUnrestricted || AllowedConnectionIds.Contains(c.Id));
        builder.Entity<CapabilityProfile>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<QueryText>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<Query>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<Plan>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<RuntimeStat>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<IngestionRun>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<AnalysisRun>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<Finding>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<HealthScore>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));
        builder.Entity<Explanation>().HasQueryFilter(
            ConnectionAccessFilter, e => AccessUnrestricted || AllowedConnectionIds.Contains(e.ConnectionId));

        // Las hijas sin connection_id heredan el filtro de su padre (la subconsulta aplica el filtro de Explanation).
        builder.Entity<ExplanationClaim>().HasQueryFilter(
            ConnectionAccessFilter, c => AccessUnrestricted || Explanations.Any(e => e.Id == c.ExplanationId));
        builder.Entity<ExplanationClaimReference>().HasQueryFilter(
            ConnectionAccessFilter, r => AccessUnrestricted || ExplanationClaims.Any(c => c.Id == r.ClaimId));
    }
}
