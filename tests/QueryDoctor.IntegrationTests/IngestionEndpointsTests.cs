using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.IntegrationTests;

// S1-11: /connections/{id}/ingestions — ingesta manual (RF-ING-06, CA-HU03.1), causa visible (RF-ING-08, CA-HU02.2),
// solo DBA (RNF-SEG-12) y la Api solo encola (invariante 2).
[Collection(PostgresGroup.Name)]
public sealed class IngestionEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    private async Task<Guid> SeedConnectionAsync(ConnectionStatus status)
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"ing-api-{Guid.NewGuid():N}"[..24],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa-en-la-api",
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Connections.Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }

    [Fact]
    public async Task UnDbaPideUnaIngestaYSeEncolaSoloConElId()
    {
        var id = await SeedConnectionAsync(ConnectionStatus.Active);
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var response = await client.PostAsync($"/connections/{id}/ingestions", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/connections/{id}/ingestions", response.Headers.Location?.OriginalString);
        Assert.Equal(1, await IngestJobsAsync(id));
    }

    [Fact]
    public async Task UnaSegundaPeticionMientrasLaPrimeraEsperaDa409()
    {
        // Auditoría B1: el segundo POST ve el trabajo en la cola ingest y no encola otro.
        var id = await SeedConnectionAsync(ConnectionStatus.Active);
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var first = await client.PostAsync($"/connections/{id}/ingestions", null);
        using var second = await client.PostAsync($"/connections/{id}/ingestions", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("AlreadyRunning", (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("cause").GetString());
        Assert.Equal(1, await IngestJobsAsync(id));
    }

    [Fact]
    public async Task ConUnaEjecucionEnCursoNoSeEncola()
    {
        var id = await SeedConnectionAsync(ConnectionStatus.Active);
        await using (var db = postgres.CreateContext())
        {
            db.IngestionRuns.Add(new IngestionRun
            {
                Id = Guid.NewGuid(),
                ConnectionId = id,
                Trigger = IngestionTrigger.Manual,
                Status = IngestionStatus.Running,
                StartedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = await _api.ClientAsync(RoleNames.Dba);
        using var response = await client.PostAsync($"/connections/{id}/ingestions", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await IngestJobsAsync(id));
    }

    [Fact]
    public async Task UnaConexionNoActivaDa409YNoEncola()
    {
        var id = await SeedConnectionAsync(ConnectionStatus.Pending);
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var response = await client.PostAsync($"/connections/{id}/ingestions", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Pending", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(0, await IngestJobsAsync(id));
    }

    [Fact]
    public async Task UnaConexionInexistenteDa404()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);
        var id = Guid.NewGuid();

        using var post = await client.PostAsync($"/connections/{id}/ingestions", null);
        using var get = await client.GetAsync($"/connections/{id}/ingestions");

        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task SinSesionEs401YUnDesarrolladorRecibe403()
    {
        var id = await SeedConnectionAsync(ConnectionStatus.Active);
        using var anonymous = await _api.ClientAsync(null);
        using var developer = await _api.ClientAsync(RoleNames.Developer);

        using var a = await anonymous.PostAsync($"/connections/{id}/ingestions", null);
        using var b = await developer.PostAsync($"/connections/{id}/ingestions", null);
        using var c = await developer.GetAsync($"/connections/{id}/ingestions");

        Assert.Equal(HttpStatusCode.Unauthorized, a.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, b.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, c.StatusCode);
        Assert.Equal(0, await IngestJobsAsync(id));
    }

    [Fact]
    public async Task UnPostDesdeOtroOrigenSeRechaza()
    {
        var id = await SeedConnectionAsync(ConnectionStatus.Active);
        using var client = await _api.ClientAsync(RoleNames.Dba);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");

        using var response = await client.PostAsync($"/connections/{id}/ingestions", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await IngestJobsAsync(id));
    }

    [Fact]
    public async Task ElHistorialMuestraLaCausaDeUnaIngestaOmitidaYLaFechaDeLaUltimaCorrecta()
    {
        // CA-HU02.2 "informa la causa" y CA-HU03.1 "veo la fecha de la última ingesta".
        var id = await SeedConnectionAsync(ConnectionStatus.Active);
        var finished = new DateTimeOffset(2026, 11, 13, 9, 30, 0, TimeSpan.Zero);
        await using (var db = postgres.CreateContext())
        {
            db.IngestionRuns.AddRange(
                new IngestionRun
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = id,
                    Trigger = IngestionTrigger.Manual,
                    Status = IngestionStatus.Succeeded,
                    StartedAt = finished.AddMinutes(-1),
                    FinishedAt = finished,
                    RowsUpserted = 12,
                },
                new IngestionRun
                {
                    Id = Guid.NewGuid(),
                    ConnectionId = id,
                    Trigger = IngestionTrigger.Manual,
                    Status = IngestionStatus.Skipped,
                    StartedAt = finished.AddMinutes(10),
                    FinishedAt = finished.AddMinutes(10),
                    ErrorCause = nameof(IngestionCause.QueryStoreDisabled),
                });
            await db.SaveChangesAsync();
        }

        using var client = await _api.ClientAsync(RoleNames.Dba);
        var runs = await client.GetFromJsonAsync<JsonElement>($"/connections/{id}/ingestions");
        var detail = await client.GetFromJsonAsync<JsonElement>($"/connections/{id}");

        Assert.Equal(2, runs.GetArrayLength());
        var latest = runs[0];
        Assert.Equal("Skipped", latest.GetProperty("status").GetString());
        Assert.Equal("QueryStoreDisabled", latest.GetProperty("cause").GetString());
        Assert.Equal(IngestionCauseMessages.For(IngestionCause.QueryStoreDisabled), latest.GetProperty("causeMessage").GetString());
        Assert.Equal(12, runs[1].GetProperty("rowsUpserted").GetInt32());
        Assert.Equal(finished, detail.GetProperty("lastIngestionAt").GetDateTimeOffset());
    }

    [Fact]
    public void LaApiNoPuedeResolverLaIngesta()
    {
        // Invariante 2: la Api encola; el lector de Query Store y el caso de uso solo existen en el Worker.
        using var scope = _api.Services.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<IQueryStoreReader>());
        Assert.Null(scope.ServiceProvider.GetService<IngestConnection>());
        Assert.Null(scope.ServiceProvider.GetService<IIngestionLock>());
        Assert.NotNull(scope.ServiceProvider.GetService<RequestIngestion>());
    }

    private async Task<long> IngestJobsAsync(Guid connectionId)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM hangfire.job j JOIN hangfire.jobqueue q ON q.jobid = j.id " +
            "WHERE q.queue = 'ingest' AND j.arguments::text LIKE @id AND j.arguments::text NOT LIKE '%credential%'",
            connection);
        command.Parameters.AddWithValue("id", $"%{connectionId}%");
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
