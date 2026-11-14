using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Connections;
using QueryDoctor.Infrastructure.Ingestion;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Security;
using QueryDoctor.Infrastructure.Telemetry;
using Testcontainers.PostgreSql;

namespace QueryDoctor.LabTests;

/// <summary>
/// PostgreSQL desechable para las pruebas de laboratorio de la ingesta. Se crea al primer uso: sin QD_LAB_SQL las
/// pruebas salen Skipped y el contenedor ni se arranca.
/// </summary>
internal static class LabPostgres
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static PostgreSqlContainer? _container;

    public static async Task<string> ConnectionStringAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_container is null)
            {
                var container = new PostgreSqlBuilder("pgvector/pgvector:pg16").WithDatabase("querydoctor_lab").Build();
                await container.StartAsync();
                await using var db = Context(container.GetConnectionString());
                await db.Database.MigrateAsync();
                _container = container;
            }

            return _container.GetConnectionString();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static AppDbContext Context(string connectionString) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
}

/// <summary>
/// La ingesta real del Worker (IngestConnection → QueryStoreReader → QueryStoreGateway → SqlClient) contra el
/// laboratorio, sobre PostgreSQL real con el almacén EF y el bloqueo consultivo. La conexión la deja Active el sondeo
/// real (ProbeConnection con EfConnectionRepository), como en producción.
/// </summary>
internal sealed class LabIngestionHarness : IDisposable
{
    private readonly SecretProtector _encryptor = new(Options.Create(new SecretProtectorOptions
    {
        MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    }));

    private LabIngestionHarness(string postgres)
    {
        Postgres = postgres;
    }

    public string Postgres { get; }

    public InMemoryStatementLogWriter StatementLog { get; } = new();

    private static ProbeOptions ProbeOptions => new()
    {
        TimeoutSeconds = 15,
        LabTrustedCertificateServers = [LabEnvironment.RequiredServer],
    };

    public static async Task<LabIngestionHarness> CreateAsync() => new(await LabPostgres.ConnectionStringAsync());

    public void Dispose() => _encryptor.Dispose();

    /// <summary>Registra la conexión en PostgreSQL y la sondea de verdad (queda Active con su perfil).</summary>
    public async Task<Guid> RegisterAndProbeAsync(string database)
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = LabEnvironment.RequiredServer,
            DatabaseName = database,
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = ConnectionCredentialsCodec.Encrypt(
                new ConnectionCredentials(LabEnvironment.Reader, LabEnvironment.ReaderPassword()), _encryptor),
            Status = ConnectionStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Cada prueba usa su propia base de PostgreSQL limpia por conexión: la UK (servidor, base) no deja repetir,
        // así que se borra una conexión previa con la misma dirección (CASCADE borra sus datos).
        await using (var db = LabPostgres.Context(Postgres))
        {
            await db.Connections.Where(c => c.Server == connection.Server && c.DatabaseName == database).ExecuteDeleteAsync();
            db.Connections.Add(connection);
            await db.SaveChangesAsync();
        }

        await using (var db = LabPostgres.Context(Postgres))
        {
            var probe = new ProbeConnection(
                new EfConnectionRepository(db),
                new CapabilityProbe(new QueryStoreGateway(StatementLog)),
                _encryptor,
                TimeProvider.System,
                ProbeOptions);
            var outcome = await probe.ExecuteAsync(connection.Id);
            Assert.Equal(ConnectionStatus.Active, outcome.Status);
        }

        StatementLog.Clear();
        return connection.Id;
    }

    public async Task<IngestionOutcome> IngestAsync(Guid connectionId, IngestionOptions? options = null)
    {
        await using var db = LabPostgres.Context(Postgres);
        var gateway = new QueryStoreGateway(StatementLog);
        var useCase = new IngestConnection(
            new EfIngestionStore(db),
            new QueryStoreReader(gateway, new CapabilityProbe(gateway)),
            new PostgresIngestionLock(Postgres),
            _encryptor,
            TimeProvider.System,
            options ?? new IngestionOptions(),
            ProbeOptions);

        return await useCase.ExecuteAsync(connectionId, IngestionTrigger.Manual);
    }

    public string Decrypt(string cipher) => _encryptor.Decrypt(cipher);
}

/// <summary>
/// S1-11 contra SQL Server 2022 real (D7). Requiere: compose sqlserver arriba, Run-Lab build + login, la carga
/// (Run-Lab -Step workload) en las últimas 24 h y lab/sql/11-probe-fixtures.sql. Docker para el PostgreSQL desechable.
/// </summary>
public sealed class IngestionLabTests
{
    private static readonly string[] IngestionStatements =
    [
        TelemetryStatementWhitelist.Keys.GetQueryStoreOptions,
        TelemetryStatementWhitelist.Keys.GetIngestionRuntimeStats,
        TelemetryStatementWhitelist.Keys.GetQueryTextById,
        TelemetryStatementWhitelist.Keys.GetPlanXmlByPlanId,
    ];

    // CA-HU03.1 y CA-HU03.2: textos, planes y métricas por intervalo; la segunda ingesta no duplica ni cambia lo
    // consolidado. RF-ING-04: las filas guardadas son únicas por (plan, tipo de ejecución, intervalo).
    [LabFact]
    public async Task UnaIngestaRealDeQdLabGuardaTodoYLaSegundaNoDuplica()
    {
        using var lab = await LabIngestionHarness.CreateAsync();
        var id = await lab.RegisterAndProbeAsync(LabEnvironment.Database);
        var before = await LabProbeHarness.QueryStoreConfigurationAsync(LabEnvironment.Database);

        var first = await lab.IngestAsync(id);

        Assert.Equal(IngestionStatus.Succeeded, first.Status);
        Assert.Empty(first.OmittedFeatures);
        Assert.NotNull(first.WatermarkAfter);
        Assert.True(first.RowsUpserted > 0);

        List<string> consolidated;
        await using (var db = LabPostgres.Context(lab.Postgres))
        {
            var queries = await db.Queries.AsNoTracking().Where(q => q.ConnectionId == id).ToListAsync();
            var plans = await db.Plans.AsNoTracking().Where(p => p.ConnectionId == id).ToListAsync();
            var texts = await db.QueryTexts.AsNoTracking().Where(t => t.ConnectionId == id).ToListAsync();
            var stats = await db.RuntimeStats.AsNoTracking().Where(r => r.ConnectionId == id).ToListAsync();

            // La carga sembrada de S1-07 está, identificada por el marcador de cada escenario en el texto.
            // Con la cuenta de mínimo privilegio (solo VIEW DATABASE STATE) SQL Server oculta los metadatos de los
            // objetos: OBJECT_NAME devuelve NULL y origin_context queda vacío (degradación esperada, RNF-SEG-02).
            Assert.All(queries, q => Assert.Matches("^[0-9A-F]{16}$", q.QueryHash!));
            Assert.All(queries, q => Assert.Null(q.OriginContext));
            Assert.NotEmpty(texts);
            Assert.All(texts, t => Assert.NotNull(t.EncryptedSqlText));
            var plainTexts = texts.Select(t => lab.Decrypt(t.EncryptedSqlText!)).ToList();
            foreach (var marker in new[] { "ESC01", "ESC02", "ESC03", "ESC04", "ESC04B", "ESC05", "ESC06" })
            {
                Assert.Contains(plainTexts, t => t.Contains($"QD-LAB:{marker} ", StringComparison.Ordinal));
            }

            // F8: el plan de 140 niveles (ESC06) se guarda completo y queda pendiente de análisis, no como no analizable.
            var esc06Text = texts.Single(t => lab.Decrypt(t.EncryptedSqlText!).Contains("QD-LAB:ESC06", StringComparison.Ordinal));
            var esc06 = queries.First(q => q.QueryTextId == esc06Text.Id);
            Assert.Null(esc06.OriginContext);
            var esc06Plan = plans.First(p => p.QueryId == esc06.Id);
            Assert.Equal(PlanAnalysisStatus.Pending, esc06Plan.AnalysisStatus);
            Assert.True(lab.Decrypt(esc06Plan.EncryptedPlanXml!).Length > 300_000);
            Assert.StartsWith("16.", esc06Plan.EngineVersion, StringComparison.Ordinal);

            Assert.NotEmpty(stats);
            Assert.Equal(stats.Count, stats.Select(s => (s.PlanId, s.ExecutionType, s.IntervalId)).Distinct().Count());
            Assert.Contains(stats, s => s.AvgTempDbSpacePages > 0); // ESC05: spill a TempDB (RF-ING-03)

            consolidated = stats.Where(s => s.IntervalId <= first.WatermarkAfter)
                .OrderBy(s => s.Id)
                .Select(s => $"{s.Id}|{s.PlanId}|{s.ExecutionType}|{s.IntervalId}|{s.ExecutionCount}|{s.AvgDurationMicroseconds}|{s.IngestedAt:O}")
                .ToList();
        }

        var second = await lab.IngestAsync(id);

        Assert.Equal(IngestionStatus.Succeeded, second.Status);
        Assert.Equal(first.WatermarkAfter, second.WatermarkBefore);
        await using (var db = LabPostgres.Context(lab.Postgres))
        {
            var stats = await db.RuntimeStats.AsNoTracking().Where(r => r.ConnectionId == id).ToListAsync();
            Assert.Equal(stats.Count, stats.Select(s => (s.PlanId, s.ExecutionType, s.IntervalId)).Distinct().Count());
            Assert.Equal(
                consolidated,
                stats.Where(s => s.IntervalId <= first.WatermarkAfter)
                    .OrderBy(s => s.Id)
                    .Select(s => $"{s.Id}|{s.PlanId}|{s.ExecutionType}|{s.IntervalId}|{s.ExecutionCount}|{s.AvgDurationMicroseconds}|{s.IngestedAt:O}")
                    .ToList());

            var plans = await db.Plans.AsNoTracking().Where(p => p.ConnectionId == id).ToListAsync();
            Assert.Equal(plans.Count, plans.Select(p => p.QueryStorePlanId).Distinct().Count());
        }

        // CA-HU02.3 / RNF-SEG-01: la ingesta no cambia Query Store y solo emite SELECT de la lista blanca.
        Assert.Equal(before, await LabProbeHarness.QueryStoreConfigurationAsync(LabEnvironment.Database));
        AssertOnlyWhitelistedSelects(lab.StatementLog);
    }

    // RF-ING-10: con TopN = 2 solo entran las dos consultas de mayor duración total.
    [LabFact]
    public async Task ElTopNLimitaLasConsultasIngeridas()
    {
        using var lab = await LabIngestionHarness.CreateAsync();
        var id = await lab.RegisterAndProbeAsync(LabEnvironment.Database);

        var outcome = await lab.IngestAsync(id, new IngestionOptions { TopN = 2 });

        Assert.Equal(IngestionStatus.Succeeded, outcome.Status);
        await using var db = LabPostgres.Context(lab.Postgres);
        Assert.Equal(2, await db.Queries.CountAsync(q => q.ConnectionId == id));
    }

    // CA-HU02.2 / RF-ING-08: Query Store OFF → omitida con causa, sin leer estadísticas y sin tocar lo guardado.
    [LabFact]
    public async Task ConQueryStoreOffLaIngestaSeOmiteInformaLaCausaYConservaLosDatos()
    {
        using var lab = await LabIngestionHarness.CreateAsync();
        var id = await lab.RegisterAndProbeAsync(LabEnvironment.DatabaseWithoutQueryStore);
        var before = await LabProbeHarness.QueryStoreConfigurationAsync(LabEnvironment.DatabaseWithoutQueryStore);

        // Datos "ya almacenados" de una ingesta anterior (por ejemplo, antes de que alguien apagara Query Store).
        await using (var db = LabPostgres.Context(lab.Postgres))
        {
            db.Queries.Add(new Query
            {
                Id = Guid.NewGuid(),
                ConnectionId = id,
                QueryStoreQueryId = 42,
                QueryHash = "00000000000000AA",
                IngestedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await db.SaveChangesAsync();
        }

        var outcome = await lab.IngestAsync(id);

        Assert.Equal(IngestionStatus.Skipped, outcome.Status);
        Assert.Equal(IngestionCause.QueryStoreDisabled, outcome.Cause);
        Assert.False(outcome.ShouldRetry);

        await using (var db = LabPostgres.Context(lab.Postgres))
        {
            Assert.Equal(42, (await db.Queries.AsNoTracking().SingleAsync(q => q.ConnectionId == id)).QueryStoreQueryId);
            var run = await db.IngestionRuns.AsNoTracking().SingleAsync(r => r.ConnectionId == id);
            Assert.Equal(IngestionStatus.Skipped, run.Status);
            Assert.Equal(nameof(IngestionCause.QueryStoreDisabled), run.ErrorCause);
        }

        // Solo se leyó el estado: ninguna sentencia de estadísticas, y Query Store sigue igual (OFF).
        var executed = lab.StatementLog.GetEntries().Select(e => e.StatementKey).ToList();
        Assert.Equal([TelemetryStatementWhitelist.Keys.GetQueryStoreOptions], executed);
        var after = await LabProbeHarness.QueryStoreConfigurationAsync(LabEnvironment.DatabaseWithoutQueryStore);
        Assert.Equal("OFF", after["actual_state_desc"]);
        Assert.Equal(before, after);
    }

    private static void AssertOnlyWhitelistedSelects(InMemoryStatementLogWriter log)
    {
        Assert.NotEmpty(log.GetEntries());
        Assert.All(log.GetEntries(), e =>
        {
            Assert.Contains(e.StatementKey, IngestionStatements);
            Assert.StartsWith("SELECT ", e.StatementText, StringComparison.Ordinal);
            Assert.True(e.Success);
            TelemetryStatementWhitelist.ValidateStatementOrThrow(e.StatementKey, e.StatementText);
        });
    }
}
