using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.IntegrationTests;

// S2-06 (RF-TRZ-05, CA-HU14.4): la Api deja en audit.audit_event los inicios de sesión exitosos y fallidos, el
// registro y la edición de conexiones y las ingestas manuales, con usuario y marca de tiempo. Nunca contraseñas.
[Collection(PostgresGroup.Name)]
public sealed class AuditEventsTests(PostgresFixture postgres) : IDisposable
{
    private const string ConnectionPassword = "S3creta!Lab#2026";

    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    private static object Body(string server) => new
    {
        platform = "SqlServer",
        server,
        databaseName = "QD_Lab",
        authenticationMethod = "SqlAuthentication",
        encryption = "Mandatory",
        username = "qd_reader",
        password = ConnectionPassword,
    };

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password) =>
        await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usuario"] = userName,
            ["password"] = password,
        }));

    private async Task<string> CreateUserAsync()
    {
        using var seed = await _api.ClientAsync(RoleNames.Dba);
        await using var db = postgres.CreateContext();
        return db.Users.OrderByDescending(u => u.CreatedAt).First().UserName!;
    }

    private List<AuditEvent> EventsFor(string userName)
    {
        using var db = postgres.CreateContext();
        return db.AuditEvents.Where(e => e.UserName == userName).OrderBy(e => e.Seq).ToList();
    }

    [Fact]
    public async Task UnLoginExitosoQuedaConUsuarioYFecha()
    {
        var userName = await CreateUserAsync();

        // ClientAsync ya inició sesión una vez con ese usuario.
        var events = EventsFor(userName);

        var login = Assert.Single(events, e => e.EventType == AuditEventType.LoginSucceeded);
        Assert.NotNull(login.UserId);
        Assert.Equal("ok", login.Outcome);
        Assert.True(login.RecordedAt > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task UnaContrasenaIncorrectaQuedaComoFallidaSinGuardarLaContrasena()
    {
        var userName = await CreateUserAsync();
        using var client = await _api.ClientAsync(role: null);

        using var response = await LoginAsync(client, userName, "Equivocada!123");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var failed = Assert.Single(EventsFor(userName), e => e.EventType == AuditEventType.LoginFailed);
        Assert.Equal("rejected", failed.Outcome);
        Assert.NotNull(failed.UserId);
        await using var db = postgres.CreateContext();
        Assert.DoesNotContain(db.AuditEvents.ToList(), e => (e.UserName ?? string.Empty).Contains("Equivocada", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnUsuarioInexistenteQuedaComoFallidoSinId()
    {
        var typed = $"nadie{Guid.NewGuid():N}";
        using var client = await _api.ClientAsync(role: null);

        await LoginAsync(client, typed, "Cualquiera!123");

        var failed = Assert.Single(EventsFor(typed));
        Assert.Equal(AuditEventType.LoginFailed, failed.EventType);
        Assert.Equal("unknown_user", failed.Outcome);
        Assert.Null(failed.UserId);
    }

    [Fact]
    public async Task ElBloqueoPorIntentosQuedaRegistrado()
    {
        var userName = await CreateUserAsync();
        using var client = await _api.ClientAsync(role: null);

        for (var i = 0; i < 6; i++)
        {
            await LoginAsync(client, userName, "Equivocada!123");
        }

        Assert.Contains(EventsFor(userName), e => e.EventType == AuditEventType.LoginLockedOut && e.Outcome == "locked");
    }

    [Fact]
    public async Task RegistroEdicionEIngestaManualQuedanConElUsuarioYLaConexion()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);
        string dba;
        await using (var db = postgres.CreateContext())
        {
            dba = db.Users.OrderByDescending(u => u.CreatedAt).First().UserName!;
        }

        var server = $"aud-{Guid.NewGuid():N}"[..20];
        using var first = await client.PostAsJsonAsync("/connections", Body(server));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var connectionId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // D3: volver a registrar una conexión fallida es su edición.
        await using (var db = postgres.CreateContext())
        {
            db.Connections.Single(c => c.Id == connectionId).Status = ConnectionStatus.Failed;
            await db.SaveChangesAsync();
        }

        using var second = await client.PostAsJsonAsync("/connections", Body(server));
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        await using (var db = postgres.CreateContext())
        {
            db.Connections.Single(c => c.Id == connectionId).Status = ConnectionStatus.Active;
            await db.SaveChangesAsync();
        }

        using var ingestion = await client.PostAsync($"/connections/{connectionId}/ingestions", null);
        Assert.Equal(HttpStatusCode.Accepted, ingestion.StatusCode);

        var events = EventsFor(dba).Where(e => e.SubjectId == connectionId).Select(e => e.EventType).ToList();
        Assert.Equal(
            [AuditEventType.ConnectionRegistered, AuditEventType.ConnectionReRegistered, AuditEventType.ManualIngestionRequested],
            events);

        await using var check = postgres.CreateContext();
        Assert.DoesNotContain(check.AuditEvents.ToList(), e => (e.UserName ?? string.Empty).Contains(ConnectionPassword, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ElLogoutQuedaRegistrado()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);
        string dba;
        await using (var db = postgres.CreateContext())
        {
            dba = db.Users.OrderByDescending(u => u.CreatedAt).First().UserName!;
        }

        using var logout = await client.PostAsync("/auth/logout", null);

        Assert.Contains(EventsFor(dba), e => e.EventType == AuditEventType.Logout);
    }
}
