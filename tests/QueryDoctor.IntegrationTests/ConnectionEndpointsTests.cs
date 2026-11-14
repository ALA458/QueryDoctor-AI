using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.IntegrationTests;

/// <summary>La Api real (Program) sobre el PostgreSQL de Testcontainers.</summary>
public sealed class ApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    public const string Origin = "https://localhost";
    public const string UserPassword = "Clave!Segura#2026";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:QueryDoctor", postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Security:SecretProtector:MasterKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    /// <summary>Cliente HTTPS (la cookie de sesión es Secure) con sesión del usuario indicado, o sin sesión.</summary>
    public async Task<HttpClient> ClientAsync(string? role, bool mustChangePassword = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(Origin),
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("Origin", Origin);

        if (role is null)
        {
            return client;
        }

        var userName = $"u{Guid.NewGuid():N}";
        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = userName,
                Email = $"{userName}@querydoctor.test",
                EmailConfirmed = true,
                MustChangePassword = mustChangePassword,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            Assert.True((await users.CreateAsync(user, UserPassword)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }

        using var login = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usuario"] = userName,
            ["password"] = UserPassword,
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }
}

// S1-09: /connections — autorización (RNF-SEG-11/12, CA-HU01.5/01.6), CSRF y contrato (docs/CONTRATO_API_CONEXIONES.md).
[Collection(PostgresGroup.Name)]
public sealed class ConnectionEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private const string Password = "S3creta!Lab#2026";

    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    private static object Body(string server, string database = "QD_Lab") => new
    {
        platform = "SqlServer",
        server,
        databaseName = database,
        authenticationMethod = "SqlAuthentication",
        encryption = "Mandatory",
        username = "qd_reader",
        password = Password,
    };

    private static string UniqueServer() => $"srv-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task SinSesionTodoEs401()
    {
        using var client = await _api.ClientAsync(role: null);

        using var list = await client.GetAsync("/connections");
        using var detail = await client.GetAsync($"/connections/{Guid.NewGuid()}");
        using var post = await client.PostAsJsonAsync("/connections", Body(UniqueServer()));

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [Fact]
    public async Task UnDesarrolladorRecibe403()
    {
        // CA-HU01.6: un Desarrollador no registra conexiones.
        using var client = await _api.ClientAsync(RoleNames.Developer);

        using var list = await client.GetAsync("/connections");
        using var post = await client.PostAsJsonAsync("/connections", Body(UniqueServer()));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [Fact]
    public async Task UnDbaConCambioDeContrasenaPendienteRecibe403()
    {
        // RNF-SEG-13. Regresión del fix de S1-06: el claim se perdía en la revalidación de la segunda petición,
        // así que se prueban varias seguidas y no solo la primera.
        using var client = await _api.ClientAsync(RoleNames.Dba, mustChangePassword: true);

        for (var i = 0; i < 3; i++)
        {
            using var post = await client.PostAsJsonAsync("/connections", Body(UniqueServer()));
            using var list = await client.GetAsync("/connections");

            Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        }
    }

    [Fact]
    public async Task LaSesionRestringidaSigueSirviendoParaVerElUsuario()
    {
        // La restricción no rompe el flujo de S1-06: /auth/me (política Session) responde y dice que falta el cambio.
        using var client = await _api.ClientAsync(RoleNames.Dba, mustChangePassword: true);

        using var first = await client.GetAsync("/auth/me");
        using var second = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task UnDbaRegistraYRecibe202ConLocationYEstadoPending()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);
        var server = UniqueServer();

        using var post = await client.PostAsJsonAsync("/connections", Body(server.ToUpperInvariant() + ",1433"));

        Assert.Equal(HttpStatusCode.Accepted, post.StatusCode);
        var accepted = await post.Content.ReadFromJsonAsync<JsonElement>();
        var id = accepted.GetProperty("id").GetGuid();
        Assert.Equal("Pending", accepted.GetProperty("status").GetString());
        Assert.False(accepted.GetProperty("replacedPrevious").GetBoolean());
        Assert.Equal($"/connections/{id}", post.Headers.Location!.AbsolutePath);

        using var detail = await client.GetAsync(post.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var json = await detail.Content.ReadAsStringAsync();
        var view = JsonDocument.Parse(json).RootElement;
        Assert.Equal(server, view.GetProperty("server").GetString());
        Assert.Equal("SqlServer", view.GetProperty("platform").GetString());
        Assert.Equal("Pending", view.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("capabilities").ValueKind);
        Assert.Equal("SQL Server 2016 (13.x)", view.GetProperty("minimumSupportedVersion").GetString());

        // Nunca se devuelven credenciales, ni cifradas.
        Assert.DoesNotContain(Password, json, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", json, StringComparison.OrdinalIgnoreCase);

        using var list = await client.GetAsync("/connections");
        var listJson = await list.Content.ReadAsStringAsync();
        Assert.Contains(id.ToString(), listJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Password, listJson, StringComparison.Ordinal);

        // El sondeo quedó encolado en Hangfire con solo el id (invariante 2: la Api no sondea).
        Assert.True(await JobEnqueuedAsync(id));
    }

    [Theory]
    [InlineData("https://evil.example", null)]
    [InlineData(null, "cross-site")]
    public async Task UnPostDesdeOtroOrigenSeRechazaYNoGuardaNada(string? origin, string? secFetchSite)
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);
        var server = UniqueServer();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connections") { Content = JsonContent.Create(Body(server)) };
        request.Headers.Remove("Origin");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (secFetchSite is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", secFetchSite);
        }

        client.DefaultRequestHeaders.Remove("Origin");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = postgres.CreateContext();
        Assert.DoesNotContain(db.Connections, c => c.Server == server);
    }

    [Fact]
    public async Task DatosInvalidosDan400ConErroresPorCampo()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var response = await client.PostAsJsonAsync("/connections", new { platform = "Oracle", server = "np:srv", databaseName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");
        foreach (var field in new[] { "platform", "server", "databaseName", "username", "password", "authenticationMethod" })
        {
            Assert.True(errors.TryGetProperty(field, out _), $"Falta el error de '{field}'.");
        }
    }

    [Fact]
    public async Task UnaBaseYaRegistradaConOtraCapitalizacionDa409()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);
        var server = UniqueServer();

        using var first = await client.PostAsJsonAsync("/connections", Body(server, "QD_Lab"));
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var second = await client.PostAsJsonAsync("/connections", Body("tcp:" + server.ToUpperInvariant(), "qd_lab"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstId, problem.GetProperty("existingConnectionId").GetGuid());
        Assert.Equal("Pending", problem.GetProperty("existingStatus").GetString());
    }

    [Fact]
    public async Task UnIdInexistenteDa404()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var response = await client.GetAsync($"/connections/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ElDetalleDeUnaConexionActivaMuestraQueryStoreYFuncionesOmitidas()
    {
        // CA-HU02.1 y RF-CFG-07 en la respuesta: el perfil lo dejaría el Worker; aquí se siembra directo.
        using var client = await _api.ClientAsync(RoleNames.Dba);
        using var post = await client.PostAsJsonAsync("/connections", Body(UniqueServer()));
        var id = (await post.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using (var db = postgres.CreateContext())
        {
            var connection = (await db.Connections.FindAsync(id))!;
            connection.Status = ConnectionStatus.Active;
            connection.LastProbedAt = DateTimeOffset.UtcNow;
            db.CapabilityProfiles.Add(new CapabilityProfile
            {
                Id = Guid.NewGuid(),
                ConnectionId = id,
                EngineVersion = "13.0.7000.253",
                Edition = "Standard Edition",
                SupportLevel = SupportLevel.CompatibleUntested,
                HasViewDatabaseState = true,
                QueryStoreActualState = "OFF",
                QueryStoreDesiredState = "OFF",
                QueryStoreReadOnlyReason = "0",
                IntervalLengthMinutes = 60,
                CaptureMode = "AUTO",
                RetentionDays = 30,
                OmittedFeaturesJson = "[\"TempDbAndLogBytes\"]",
                ProbedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var detail = await client.GetAsync($"/connections/{id}");
        var capabilities = (await detail.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("capabilities");

        Assert.Equal("CompatibleUntested", capabilities.GetProperty("supportLevel").GetString());
        Assert.Equal(JsonValueKind.Null, capabilities.GetProperty("hasExcessivePermissions").ValueKind);
        var queryStore = capabilities.GetProperty("queryStore");
        Assert.Equal("OFF", queryStore.GetProperty("actualState").GetString());
        Assert.Equal(60, queryStore.GetProperty("intervalLengthMinutes").GetInt32());
        Assert.Equal("AUTO", queryStore.GetProperty("captureMode").GetString());
        Assert.Equal(30, queryStore.GetProperty("retentionDays").GetInt32());
        Assert.False(queryStore.GetProperty("canIngest").GetBoolean());
        var omitted = Assert.Single(capabilities.GetProperty("omittedFeatures").EnumerateArray());
        Assert.Equal("TempDbAndLogBytes", omitted.GetProperty("code").GetString());
    }

    [Fact]
    public void LaApiNoPuedeResolverElGatewayNiElProbe()
    {
        // Invariante 2: la Api nunca abre conexiones a bases objetivo.
        using var scope = _api.Services.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<ITelemetrySource>());
        Assert.Null(scope.ServiceProvider.GetService<ICapabilityProbe>());
        Assert.Null(scope.ServiceProvider.GetService<ProbeConnection>());
        Assert.NotNull(scope.ServiceProvider.GetService<RegisterConnection>());
    }

    private async Task<bool> JobEnqueuedAsync(Guid connectionId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM hangfire.job j JOIN hangfire.jobqueue q ON q.jobid = j.id " +
            "WHERE q.queue = 'probe' AND j.arguments::text LIKE @id AND j.arguments::text NOT LIKE @pw",
            connection);
        command.Parameters.AddWithValue("id", $"%{connectionId}%");
        command.Parameters.AddWithValue("pw", $"%{Password}%");
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }
}
