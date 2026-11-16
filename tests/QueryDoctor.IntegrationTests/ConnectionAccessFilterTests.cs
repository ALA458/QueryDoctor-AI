using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.UnitTests.Security;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// S2-12 (RNF-SEG-12): el filtro global por conexión, sobre PostgreSQL real. Dos conexiones A y B con toda su cadena de
/// datos; cada tabla filtrada se lee con cada contexto de acceso.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class ConnectionAccessFilterTests(PostgresFixture postgres)
{
    private AppDbContext Context(IAccessScope scope) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(postgres.ConnectionString).Options, scope);

    [Fact]
    public async Task UnDesarrolladorSoloVeSuConexionEnTodasLasTablas()
    {
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();

        await using var db = Context(TestAccessScope.Developer(a.ConnectionId));

        Assert.Equal([a.ConnectionId], await VisibleAsync(db, a, b));
    }

    [Theory]
    [MemberData(nameof(FullAccess))]
    public async Task ElDbaYElSistemaVenTodo(string name)
    {
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();

        await using var db = Context(Scope(name));

        Assert.Equal([a.ConnectionId, b.ConnectionId], await VisibleAsync(db, a, b));
    }

    [Theory]
    [MemberData(nameof(NoAccess))]
    public async Task SinUsuarioOSinAsignacionesNoSeVeNada(string name)
    {
        // Denegación por defecto: "sin usuario" nunca es acceso completo.
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();

        await using var db = Context(Scope(name));

        Assert.Empty(await VisibleAsync(db, a, b));
    }

    [Fact]
    public async Task UnaConexionNoAsignadaNoAparecePorIdDirecto()
    {
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();

        await using var db = Context(TestAccessScope.Developer(a.ConnectionId));

        Assert.NotNull(await db.Connections.FindAsync(a.ConnectionId));
        Assert.Null(await db.Connections.FindAsync(b.ConnectionId));
        Assert.Null(await db.Queries.FirstOrDefaultAsync(q => q.Id == b.QueryId));
        Assert.Null(await db.Plans.FirstOrDefaultAsync(p => p.Id == b.PlanId));
        Assert.False(await db.Findings.AnyAsync(f => f.QueryId == b.QueryId));
        Assert.False(await db.ExplanationClaims.AnyAsync(c => c.ExplanationId == b.ExplanationId));
    }

    public static TheoryData<string> FullAccess() => new() { "dba", "system" };

    public static TheoryData<string> NoAccess() => new() { "none", "developer-sin-asignaciones" };

    private static IAccessScope Scope(string name) => name switch
    {
        "dba" => TestAccessScope.Dba,
        "system" => SystemAccessScope.Instance,
        "none" => TestAccessScope.None,
        "developer-sin-asignaciones" => TestAccessScope.Developer(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>
    /// Conexiones (entre A y B) visibles en CADA tabla filtrada. Si una tabla deja ver algo distinto que las demás, la
    /// prueba falla con el nombre de la tabla.
    /// </summary>
    private static async Task<Guid[]> VisibleAsync(AppDbContext db, Seeded a, Seeded b)
    {
        Guid[] ids = [a.ConnectionId, b.ConnectionId];
        Guid[] explanations = [a.ExplanationId, b.ExplanationId];

        var perTable = new Dictionary<string, Guid[]>
        {
            ["connection"] = await db.Connections.Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(),
            ["capability_profile"] = await db.CapabilityProfiles.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["query_text"] = await db.QueryTexts.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["query"] = await db.Queries.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["plan"] = await db.Plans.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["runtime_stat"] = await db.RuntimeStats.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["ingestion_run"] = await db.IngestionRuns.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["analysis_run"] = await db.AnalysisRuns.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["finding"] = await db.Findings.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["health_score"] = await db.HealthScores.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["explanation"] = await db.Explanations.Where(x => ids.Contains(x.ConnectionId)).Select(x => x.ConnectionId).ToArrayAsync(),
            ["claim"] = await db.ExplanationClaims.Where(x => explanations.Contains(x.ExplanationId))
                .Select(x => x.ExplanationId == a.ExplanationId ? a.ConnectionId : b.ConnectionId).ToArrayAsync(),
            ["claim_reference"] = await db.ExplanationClaimReferences.Where(x => x.ClaimId == a.ClaimId || x.ClaimId == b.ClaimId)
                .Select(x => x.ClaimId == a.ClaimId ? a.ConnectionId : b.ConnectionId).ToArrayAsync(),
        };

        var expected = perTable["connection"].Order().ToArray();
        foreach (var (table, visible) in perTable)
        {
            Assert.True(visible.Order().SequenceEqual(expected), $"La tabla {table} no respeta el filtro por conexión.");
        }

        return ids.Where(expected.Contains).ToArray();
    }

    private sealed record Seeded(Guid ConnectionId, Guid QueryId, Guid PlanId, Guid ExplanationId, Guid ClaimId);

    /// <summary>Una conexión con un registro en cada tabla filtrada, sembrada con acceso Sistema.</summary>
    private async Task<Seeded> SeedConnectionAsync()
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"rbac-{Guid.NewGuid():N}"[..21],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa",
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var text = new QueryText { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryStoreTextId = 1, IngestedAt = now };
        var query = new Query { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryStoreQueryId = 1, QueryTextId = text.Id, IngestedAt = now };
        var plan = new Plan { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryId = query.Id, QueryStorePlanId = 1, IngestedAt = now };
        var run = new IngestionRun { Id = Guid.NewGuid(), ConnectionId = connection.Id, Trigger = IngestionTrigger.Manual, Status = IngestionStatus.Succeeded, StartedAt = now, FinishedAt = now };
        var thresholds = new ThresholdSet { Id = Guid.NewGuid(), CatalogVersion = "test", ConfigurationHash = Guid.NewGuid().ToString("N"), CreatedAt = now };
        var analysis = new AnalysisRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = query.Id,
            PlanId = plan.Id,
            ThresholdSetId = thresholds.Id,
            CatalogVersion = "test",
            Status = AnalysisRunStatus.Succeeded,
            FindingsCount = 1,
            StartedAt = now,
            FinishedAt = now,
        };
        var explanation = new Explanation
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = query.Id,
            State = ExplanationState.Pending,
            CorrelationId = Guid.NewGuid(),
            RequestedAt = now,
        };
        var claim = new ExplanationClaim { Id = Guid.NewGuid(), ExplanationId = explanation.Id, ClaimType = "OBSERVACION", EncryptedText = "cifrado" };
        claim.References.Add(new ExplanationClaimReference { Id = Guid.NewGuid(), ClaimId = claim.Id, FindingCode = "F-0001" });

        db.Connections.Add(connection);
        db.CapabilityProfiles.Add(new CapabilityProfile { Id = Guid.NewGuid(), ConnectionId = connection.Id, EngineVersion = "16.0", SupportLevel = SupportLevel.Tested, ProbedAt = now });
        db.QueryTexts.Add(text);
        db.Queries.Add(query);
        db.Plans.Add(plan);
        db.RuntimeStats.Add(new RuntimeStat { ConnectionId = connection.Id, PlanId = plan.Id, ExecutionType = ExecutionType.Regular, IntervalId = 1, IntervalStart = now, IntervalEnd = now, ExecutionCount = 1, IngestedAt = now });
        db.IngestionRuns.Add(run);
        db.ThresholdSets.Add(thresholds);
        db.AnalysisRuns.Add(analysis);
        db.Findings.Add(new Finding
        {
            Id = Guid.NewGuid(),
            AnalysisRunId = analysis.Id,
            ConnectionId = connection.Id,
            QueryId = query.Id,
            PlanId = plan.Id,
            FindingCode = "F-0001",
            RuleId = "REG-01",
            RuleVersion = "1.0.0",
            CatalogVersion = "test",
            SchemaVersion = "1.0.0",
            Severity = FindingSeverity.Alta,
            EvidenceClass = EvidenceClass.Estimada,
            EncryptedEvidenceJson = "cifrado",
            AnalyzedAt = now,
        });
        db.HealthScores.Add(new HealthScore
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = query.Id,
            IngestionRunId = run.Id,
            WindowStart = now,
            WindowEnd = now,
            ParametersHash = "hash",
            CalculatedAt = now,
        });
        db.Explanations.Add(explanation);
        db.ExplanationClaims.Add(claim);
        await db.SaveChangesAsync();

        return new Seeded(connection.Id, query.Id, plan.Id, explanation.Id, claim.Id);
    }
}
