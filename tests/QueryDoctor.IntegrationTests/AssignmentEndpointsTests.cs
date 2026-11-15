using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// S2-12 (RNF-SEG-12, RF-TRZ-05): el DBA asigna y quita conexiones a los Desarrolladores, y cada cambio real queda en
/// la auditoría encadenada.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class AssignmentEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task ElDbaAsignaYQuitaYElDesarrolladorLoNotaEnLaSiguienteSolicitud()
    {
        var b = await SeedConnectionAsync();
        using var dba = await _api.ClientAsync(RoleNames.Dba);
        var (developer, developerId) = await _api.SessionAsync([RoleNames.Developer]);

        Assert.Equal(HttpStatusCode.NotFound, (await developer.GetAsync($"/connections/{b}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await dba.PutAsync($"/connections/{b}/assignments/{developerId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await developer.GetAsync($"/connections/{b}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await dba.DeleteAsync($"/connections/{b}/assignments/{developerId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await developer.GetAsync($"/connections/{b}")).StatusCode);
    }

    [Fact]
    public async Task AsignarEsIdempotenteYCadaCambioRealSeAuditaUnaVez()
    {
        var connectionId = await SeedConnectionAsync();
        var (dba, dbaId) = await _api.SessionAsync([RoleNames.Dba]);
        var (_, developerId) = await _api.SessionAsync([RoleNames.Developer]);

        using var first = await dba.PutAsync($"/connections/{connectionId}/assignments/{developerId}", null);
        using var again = await dba.PutAsync($"/connections/{connectionId}/assignments/{developerId}", null);
        using var removed = await dba.DeleteAsync($"/connections/{connectionId}/assignments/{developerId}");
        using var removedAgain = await dba.DeleteAsync($"/connections/{connectionId}/assignments/{developerId}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, removedAgain.StatusCode);

        await using var db = postgres.CreateContext();
        var events = db.AuditEvents.Where(e => e.SubjectId == connectionId).OrderBy(e => e.Seq).ToList();
        Assert.Equal([AuditEventType.ConnectionAssigned, AuditEventType.ConnectionUnassigned], events.Select(e => e.EventType));
        Assert.All(events, e =>
        {
            Assert.Equal(dbaId, e.UserId);
            Assert.Equal(AuditEventDraftSubject, e.SubjectType);
            Assert.Equal(developerId, e.RelatedUserId);
            Assert.Equal("ok", e.Outcome);
        });
    }

    [Fact]
    public async Task LaCadenaDeAuditoriaSigueIntactaConLosEventosNuevos()
    {
        var connectionId = await SeedConnectionAsync();
        using var dba = await _api.ClientAsync(RoleNames.Dba);
        var (_, developerId) = await _api.SessionAsync([RoleNames.Developer]);

        await dba.PutAsync($"/connections/{connectionId}/assignments/{developerId}", null);
        await dba.DeleteAsync($"/connections/{connectionId}/assignments/{developerId}");
        using var verify = await dba.GetAsync("/audit/verify");
        using var listed = await dba.GetAsync("/audit?type=ConnectionAssigned");

        Assert.True((await verify.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("intact").GetBoolean());
        var assigned = (await listed.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()
            .Single(e => e.GetProperty("subject_id").GetGuid() == connectionId);
        Assert.Equal(developerId, assigned.GetProperty("related_user_id").GetGuid());
    }

    [Fact]
    public async Task ElListadoMuestraAQuienEstaAsignada()
    {
        var connectionId = await SeedConnectionAsync();
        using var dba = await _api.ClientAsync(RoleNames.Dba);
        var (_, developerId) = await _api.SessionAsync([RoleNames.Developer]);
        await dba.PutAsync($"/connections/{connectionId}/assignments/{developerId}", null);

        var body = await (await dba.GetAsync($"/connections/{connectionId}/assignments")).Content.ReadFromJsonAsync<JsonElement>();

        var only = Assert.Single(body.EnumerateArray());
        Assert.Equal(developerId, only.GetProperty("userId").GetGuid());
        Assert.StartsWith("u", only.GetProperty("userName").GetString(), StringComparison.Ordinal);
        Assert.True(only.TryGetProperty("assignedAt", out _));
    }

    [Fact]
    public async Task DatosQueNoCorrespondenDan404O400SinCrearNada()
    {
        var connectionId = await SeedConnectionAsync();
        var (dba, dbaId) = await _api.SessionAsync([RoleNames.Dba]);
        var (_, developerId) = await _api.SessionAsync([RoleNames.Developer]);

        using var unknownConnection = await dba.PutAsync($"/connections/{Guid.NewGuid()}/assignments/{developerId}", null);
        using var unknownUser = await dba.PutAsync($"/connections/{connectionId}/assignments/{Guid.NewGuid()}", null);
        using var toDba = await dba.PutAsync($"/connections/{connectionId}/assignments/{dbaId}", null);
        using var listUnknown = await dba.GetAsync($"/connections/{Guid.NewGuid()}/assignments");

        Assert.Equal(HttpStatusCode.NotFound, unknownConnection.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownUser.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, toDba.StatusCode);
        Assert.Equal("NotDeveloper", (await toDba.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cause").GetString());
        Assert.Equal(HttpStatusCode.NotFound, listUnknown.StatusCode);

        await using var db = postgres.CreateContext();
        Assert.False(db.UserConnections.Any(uc => uc.ConnectionId == connectionId));
        Assert.False(db.AuditEvents.Any(e => e.SubjectId == connectionId));
    }

    [Fact]
    public async Task ElDbaListaSoloALosDesarrolladores()
    {
        var (dba, dbaId) = await _api.SessionAsync([RoleNames.Dba]);
        var (_, developerId) = await _api.SessionAsync([RoleNames.Developer]);

        using var developers = await dba.GetAsync($"/users?role={RoleNames.Developer}");
        using var withoutRole = await dba.GetAsync("/users");

        var ids = (await developers.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(developerId, ids);
        Assert.DoesNotContain(dbaId, ids);
        Assert.Equal(HttpStatusCode.BadRequest, withoutRole.StatusCode);
    }

    [Fact]
    public async Task ElDesarrolladorNoGestionaAsignacionesYSinSesionEs401()
    {
        var connectionId = await SeedConnectionAsync();
        var (developer, developerId) = await _api.SessionAsync([RoleNames.Developer]);
        using var anonymous = await _api.ClientAsync(role: null);

        foreach (var client in new[] { developer, anonymous })
        {
            var expected = client == developer ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized;
            Assert.Equal(expected, (await client.PutAsync($"/connections/{connectionId}/assignments/{developerId}", null)).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync($"/connections/{connectionId}/assignments/{developerId}")).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"/connections/{connectionId}/assignments")).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"/users?role={RoleNames.Developer}")).StatusCode);
        }

        await using var db = postgres.CreateContext();
        Assert.False(db.UserConnections.Any(uc => uc.ConnectionId == connectionId));
    }

    [Fact]
    public async Task UnaAsignacionDesdeOtroOrigenSeRechaza()
    {
        // CSRF: UseSameOriginForms cubre PUT y DELETE.
        var connectionId = await SeedConnectionAsync();
        using var dba = await _api.ClientAsync(RoleNames.Dba);
        var (_, developerId) = await _api.SessionAsync([RoleNames.Developer]);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/connections/{connectionId}/assignments/{developerId}");
        request.Headers.Remove("Origin");
        request.Headers.Add("Origin", "https://evil.example");

        using var response = await dba.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = postgres.CreateContext();
        Assert.False(db.UserConnections.Any(uc => uc.ConnectionId == connectionId));
    }

    private const string AuditEventDraftSubject = "connection";

    private async Task<Guid> SeedConnectionAsync()
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"asg-{Guid.NewGuid():N}"[..20],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa",
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Connections.Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }
}
