using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Explanations;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.IntegrationTests;

// S2-05: /queries/{id}/explanations contra PostgreSQL real con la migración ExplicacionesIa. La Api solo guarda y
// encola explain con el id (invariante 2); sin hallazgos lo indica sin encolar (CA-HU09.2); con la IA caída se ven
// los hallazgos y el aviso, y las demás vistas siguen funcionando (CA-HU12.1, CA-HU12.2).
[Collection(PostgresGroup.Name)]
public sealed class ExplanationEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    private async Task<Guid> SeedQueryAsync(int findings)
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"exp-{Guid.NewGuid():N}"[..20],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa",
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var query = new Query { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryStoreQueryId = Random.Shared.NextInt64(1, long.MaxValue), IngestedAt = now };
        var plan = new Plan { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryId = query.Id, QueryStorePlanId = Random.Shared.NextInt64(1, long.MaxValue), IngestedAt = now };
        db.Connections.Add(connection);
        db.Queries.Add(query);
        db.Plans.Add(plan);

        if (findings > 0)
        {
            var thresholds = new ThresholdSet { Id = Guid.NewGuid(), CatalogVersion = "test", ConfigurationHash = Guid.NewGuid().ToString("N"), CreatedAt = now };
            var run = new AnalysisRun
            {
                Id = Guid.NewGuid(),
                ConnectionId = connection.Id,
                QueryId = query.Id,
                PlanId = plan.Id,
                ThresholdSetId = thresholds.Id,
                CatalogVersion = "test",
                Status = AnalysisRunStatus.Succeeded,
                FindingsCount = findings,
                StartedAt = now,
                FinishedAt = now,
            };
            db.ThresholdSets.Add(thresholds);
            db.AnalysisRuns.Add(run);
            for (var i = 0; i < findings; i++)
            {
                db.Findings.Add(new Finding
                {
                    Id = Guid.NewGuid(),
                    AnalysisRunId = run.Id,
                    ConnectionId = connection.Id,
                    QueryId = query.Id,
                    PlanId = plan.Id,
                    FindingCode = $"F-{i:D4}",
                    RuleId = "REG-01",
                    RuleVersion = "1.0.0",
                    CatalogVersion = "test",
                    SchemaVersion = "1.0.0",
                    Severity = FindingSeverity.Alta,
                    EvidenceClass = EvidenceClass.Estimada,
                    EncryptedEvidenceJson = "no-se-descifra-en-la-api",
                    AnalyzedAt = now,
                });
            }
        }

        await db.SaveChangesAsync();
        return query.Id;
    }

    private async Task<Explanation> AddExplanationAsync(Guid queryId, Action<Explanation> configure)
    {
        await using var db = postgres.CreateContext();
        var connectionId = db.Queries.Single(q => q.Id == queryId).ConnectionId;
        var explanation = new Explanation
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            QueryId = queryId,
            CorrelationId = Guid.NewGuid(),
            RequestedAt = DateTimeOffset.UtcNow,
        };
        configure(explanation);
        db.Explanations.Add(explanation);
        await db.SaveChangesAsync();
        return explanation;
    }

    private async Task<List<string>> ExplainJobArgumentsAsync(Guid explanationIdOrQueryId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT j.arguments::text FROM hangfire.job j JOIN hangfire.jobqueue q ON q.jobid = j.id " +
            "WHERE q.queue = 'explain' AND j.arguments::text LIKE @id",
            connection);
        command.Parameters.AddWithValue("id", $"%{explanationIdOrQueryId}%");
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    [Fact]
    public async Task ConHallazgosSeAceptaYSeEncolaExplainSoloConElId()
    {
        var queryId = await SeedQueryAsync(findings: 2);
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var response = await client.PostAsync($"/queries/{queryId}/explanations", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/queries/{queryId}/explanations/latest", response.Headers.Location?.OriginalString);
        var explanationId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("explanation_id").GetGuid();

        // Invariante 2 / §8.1: el único argumento con datos es el id de la explicación.
        var arguments = Assert.Single(await ExplainJobArgumentsAsync(explanationId));
        Assert.True(arguments.Length < 80, arguments);

        await using var db = postgres.CreateContext();
        Assert.Equal(ExplanationState.Pending, db.Explanations.Single(e => e.Id == explanationId).State);
    }

    [Fact]
    public async Task UnaSegundaPeticionMientrasLaPrimeraEstaPendienteDa409()
    {
        var queryId = await SeedQueryAsync(findings: 1);
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var first = await client.PostAsync($"/queries/{queryId}/explanations", null);
        using var second = await client.PostAsync($"/queries/{queryId}/explanations", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("AlreadyInProgress", (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cause").GetString());
    }

    [Fact]
    public async Task SinHallazgosNoSeGeneraYSeIndicaSinEncolar()
    {
        // CA-HU09.2.
        var queryId = await SeedQueryAsync(findings: 0);
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var response = await client.PostAsync($"/queries/{queryId}/explanations", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ExplanationCodes.StateNoFindings, body.GetProperty("status").GetString());
        Assert.Equal(ExplanationCodes.NoFindingsNotice, body.GetProperty("notice").GetString());
        await using var db = postgres.CreateContext();
        Assert.False(db.Explanations.Any(e => e.QueryId == queryId));
    }

    [Fact]
    public async Task ConLaIaNoDisponibleSeVenLosHallazgosElAvisoYLasDemasVistasFuncionan()
    {
        // CA-HU12.1 y CA-HU12.2.
        var queryId = await SeedQueryAsync(findings: 2);
        await AddExplanationAsync(queryId, e =>
        {
            e.State = ExplanationState.Unavailable;
            e.Cause = ExplanationCause.Timeout;
            e.CompletedAt = DateTimeOffset.UtcNow;
        });
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var latest = await client.GetAsync($"/queries/{queryId}/explanations/latest");
        using var connections = await client.GetAsync("/connections");

        Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        var view = await latest.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ExplanationCodes.StateUnavailable, view.GetProperty("status").GetString());
        Assert.Equal("Timeout", view.GetProperty("cause").GetString());
        Assert.Equal(ExplanationCodes.UnavailableNotice, view.GetProperty("notice").GetString());
        Assert.Equal(2, view.GetProperty("findings").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, connections.StatusCode);
    }

    [Fact]
    public async Task UnaTrazadaSeDevuelveDescifradaConElAvisoPersistente()
    {
        var queryId = await SeedQueryAsync(findings: 1);
        var encryptor = _api.Services.GetRequiredService<IFieldEncryptor>();
        var body = new ExplanationBody(
            "TRAZADA",
            "Trazada a evidencia del motor; no equivale a un diagnóstico confirmado",
            "Resumen",
            []);
        var explanation = await AddExplanationAsync(queryId, e =>
        {
            e.State = ExplanationState.Completed;
            e.ValidationStatus = "TRAZADA";
            e.EncryptedBody = encryptor.Encrypt(body.ToJson());
            e.ContextHash = new string('a', 64);
            e.CompletedAt = DateTimeOffset.UtcNow;
        });
        await using (var db = postgres.CreateContext())
        {
            var claimId = Guid.NewGuid();
            db.ExplanationClaims.Add(new ExplanationClaim
            {
                Id = claimId,
                ExplanationId = explanation.Id,
                ClaimIndex = 0,
                ClaimType = "OBSERVACION",
                EncryptedText = encryptor.Encrypt("Afirmación"),
                Passed = true,
                References = [new ExplanationClaimReference { Id = Guid.NewGuid(), ClaimId = claimId, FindingCode = "F-0000", NodeIdsJson = "[3,7]" }],
            });
            await db.SaveChangesAsync();
        }
        using var client = await _api.ClientAsync(RoleNames.Dba);

        var view = await client.GetFromJsonAsync<JsonElement>($"/queries/{queryId}/explanations/latest");

        Assert.Equal(ExplanationCodes.StateCompleted, view.GetProperty("status").GetString());
        Assert.Equal("TRAZADA", view.GetProperty("validation_status").GetString());
        Assert.Equal(body.PersistentNotice, view.GetProperty("notice").GetString());
        var claim = view.GetProperty("claims")[0];
        Assert.Equal("Afirmación", claim.GetProperty("statement").GetString());

        // RF-DASH-05: cada afirmación enlaza sus hallazgos y los nodos del plan.
        var reference = claim.GetProperty("references")[0];
        Assert.Equal("F-0000", reference.GetProperty("finding_code").GetString());
        Assert.Equal([3, 7], reference.GetProperty("node_ids").EnumerateArray().Select(n => n.GetInt32()));
    }

    [Fact]
    public async Task UnaConsultaInexistenteDa404()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var post = await client.PostAsync($"/queries/{Guid.NewGuid()}/explanations", null);
        using var get = await client.GetAsync($"/queries/{Guid.NewGuid()}/explanations/latest");

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task UnDesarrolladorTodaviaNoPuedePedirExplicaciones()
    {
        // Denegación por defecto (RNF-SEG-12): el acceso por conexión asignada llega en S2-12.
        var queryId = await SeedQueryAsync(findings: 1);
        using var client = await _api.ClientAsync(RoleNames.Developer);

        using var response = await client.PostAsync($"/queries/{queryId}/explanations", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
