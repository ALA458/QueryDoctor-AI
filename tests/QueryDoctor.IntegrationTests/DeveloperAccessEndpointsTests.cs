using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// S2-12 (RNF-SEG-11/12, CA-HU01.5/01.6): el Desarrollador por la Api real. Conexiones A y B; el Desarrollador solo tiene
/// A. "Hecho cuando": no ve conexiones no asignadas ni por la UI ni por API directa.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class DeveloperAccessEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task ElDesarrolladorSoloListaLasConexionesAsignadas()
    {
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();
        var (developer, userId) = await _api.SessionAsync([RoleNames.Developer]);
        await AssignAsync(userId, a.ConnectionId);

        var ids = await ListedIdsAsync(developer);

        Assert.Equal([a.ConnectionId], ids);
        Assert.DoesNotContain(b.ConnectionId, ids);
    }

    [Fact]
    public async Task PorIdDirectoLoNoAsignadoEs404IgualQueLoInexistente()
    {
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();
        var (developer, userId) = await _api.SessionAsync([RoleNames.Developer]);
        await AssignAsync(userId, a.ConnectionId);

        // Lo asignado se ve.
        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync($"/connections/{a.ConnectionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync($"/connections/{a.ConnectionId}/ingestions")).StatusCode);

        // Lo no asignado responde exactamente como algo que no existe: no revela que existe.
        foreach (var (assigned, unknown) in new[]
        {
            ($"/connections/{b.ConnectionId}", $"/connections/{Guid.NewGuid()}"),
            ($"/connections/{b.ConnectionId}/ingestions", $"/connections/{Guid.NewGuid()}/ingestions"),
            ($"/queries/{b.QueryId}/explanations/latest", $"/queries/{Guid.NewGuid()}/explanations/latest"),
        })
        {
            using var hidden = await developer.GetAsync(assigned);
            using var missing = await developer.GetAsync(unknown);
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.Equal(await DetailAsync(missing), await DetailAsync(hidden));
        }
    }

    [Fact]
    public async Task ElDesarrolladorPideExplicacionesSoloDeLoAsignado()
    {
        // CU-05: el Desarrollador es actor principal de pedir la explicación (D1). De B no puede ni pedirla ni leerla.
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();
        var (developer, userId) = await _api.SessionAsync([RoleNames.Developer]);
        await AssignAsync(userId, a.ConnectionId);

        using var own = await developer.PostAsync($"/queries/{a.QueryId}/explanations", null);
        using var other = await developer.PostAsync($"/queries/{b.QueryId}/explanations", null);
        using var ownLatest = await developer.GetAsync($"/queries/{a.QueryId}/explanations/latest");

        Assert.Equal(HttpStatusCode.Accepted, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownLatest.StatusCode);
        await using var db = postgres.CreateContext();
        Assert.False(db.Explanations.Any(e => e.QueryId == b.QueryId));
    }

    [Fact]
    public async Task ElDesarrolladorNoRegistraNiIngiereNiSiquieraLoAsignado()
    {
        // RNF-SEG-12 / CA-HU01.6: registrar e iniciar ingestas es solo DBA.
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();
        var (developer, userId) = await _api.SessionAsync([RoleNames.Developer]);
        await AssignAsync(userId, a.ConnectionId);

        using var register = await developer.PostAsJsonAsync("/connections", new { platform = "SqlServer", server = "x", databaseName = "y" });
        using var ingestOwn = await developer.PostAsync($"/connections/{a.ConnectionId}/ingestions", null);
        using var ingestOther = await developer.PostAsync($"/connections/{b.ConnectionId}/ingestions", null);

        Assert.Equal(HttpStatusCode.Forbidden, register.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ingestOwn.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, ingestOther.StatusCode);
    }

    [Fact]
    public async Task ElDbaVeAmbasSinAsignaciones()
    {
        var a = await SeedConnectionAsync();
        var b = await SeedConnectionAsync();
        using var dba = await _api.ClientAsync(RoleNames.Dba);

        var ids = await ListedIdsAsync(dba);

        Assert.Contains(a.ConnectionId, ids);
        Assert.Contains(b.ConnectionId, ids);
        Assert.Equal(HttpStatusCode.OK, (await dba.GetAsync($"/connections/{b.ConnectionId}")).StatusCode);
    }

    [Fact]
    public async Task AsignarOQuitarSurteEfectoEnLaSiguienteSolicitud()
    {
        // Las asignaciones se leen de la base en cada solicitud: no viajan en la cookie (misma sesión todo el tiempo).
        var b = await SeedConnectionAsync();
        var (developer, userId) = await _api.SessionAsync([RoleNames.Developer]);

        Assert.Equal(HttpStatusCode.NotFound, (await developer.GetAsync($"/connections/{b.ConnectionId}")).StatusCode);

        await AssignAsync(userId, b.ConnectionId);
        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync($"/connections/{b.ConnectionId}")).StatusCode);
        Assert.Contains(b.ConnectionId, await ListedIdsAsync(developer));

        await UnassignAsync(userId, b.ConnectionId);
        Assert.Equal(HttpStatusCode.NotFound, (await developer.GetAsync($"/connections/{b.ConnectionId}")).StatusCode);
        Assert.DoesNotContain(b.ConnectionId, await ListedIdsAsync(developer));
    }

    [Fact]
    public async Task SinSesionTodoEs401()
    {
        // CA-HU01.5 / RNF-SEG-11.
        var a = await SeedConnectionAsync();
        using var anonymous = await _api.ClientAsync(role: null);

        foreach (var path in Reads(a))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/queries/{a.QueryId}/explanations", null)).StatusCode);
    }

    [Fact]
    public async Task UnUsuarioSinRolOConCambioPendienteNoLee()
    {
        // Denegación por defecto: tener sesión no basta.
        var a = await SeedConnectionAsync();
        var (noRole, noRoleId) = await _api.SessionAsync([]);
        var (pending, pendingId) = await _api.SessionAsync([RoleNames.Developer], mustChangePassword: true);
        await AssignAsync(noRoleId, a.ConnectionId);
        await AssignAsync(pendingId, a.ConnectionId);

        foreach (var path in Reads(a))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await noRole.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync(path)).StatusCode);
        }
    }

    [Fact]
    public async Task ConLosDosRolesGanaDba()
    {
        var b = await SeedConnectionAsync();
        var (both, _) = await _api.SessionAsync([RoleNames.Dba, RoleNames.Developer]);

        Assert.Equal(HttpStatusCode.OK, (await both.GetAsync($"/connections/{b.ConnectionId}")).StatusCode);
    }

    private static string[] Reads(Seeded s) =>
    [
        "/connections",
        $"/connections/{s.ConnectionId}",
        $"/connections/{s.ConnectionId}/ingestions",
        $"/queries/{s.QueryId}/explanations/latest",
    ];

    private static async Task<Guid[]> ListedIdsAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/connections");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToArray();
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }

    private async Task AssignAsync(Guid userId, Guid connectionId)
    {
        await using var db = postgres.CreateContext();
        db.UserConnections.Add(new UserConnection { UserId = userId, ConnectionId = connectionId, AssignedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task UnassignAsync(Guid userId, Guid connectionId)
    {
        await using var db = postgres.CreateContext();
        db.UserConnections.Remove(db.UserConnections.Single(uc => uc.UserId == userId && uc.ConnectionId == connectionId));
        await db.SaveChangesAsync();
    }

    private sealed record Seeded(Guid ConnectionId, Guid QueryId);

    /// <summary>Conexión activa con perfil, una ingesta, una consulta con plan y un análisis con un hallazgo.</summary>
    private async Task<Seeded> SeedConnectionAsync()
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"dev-{Guid.NewGuid():N}"[..20],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa",
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var query = new Query { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryStoreQueryId = 1, IngestedAt = now };
        var plan = new Plan { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryId = query.Id, QueryStorePlanId = 1, IngestedAt = now };
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

        db.Connections.Add(connection);
        db.CapabilityProfiles.Add(new CapabilityProfile { Id = Guid.NewGuid(), ConnectionId = connection.Id, EngineVersion = "16.0", SupportLevel = SupportLevel.Tested, ProbedAt = now });
        db.IngestionRuns.Add(new IngestionRun { Id = Guid.NewGuid(), ConnectionId = connection.Id, Trigger = IngestionTrigger.Manual, Status = IngestionStatus.Succeeded, StartedAt = now, FinishedAt = now });
        db.Queries.Add(query);
        db.Plans.Add(plan);
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
            EncryptedEvidenceJson = "no-se-descifra-en-la-api",
            AnalyzedAt = now,
        });
        await db.SaveChangesAsync();

        return new Seeded(connection.Id, query.Id);
    }
}
