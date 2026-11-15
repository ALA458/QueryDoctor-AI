using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure.Explanations;

namespace QueryDoctor.IntegrationTests;

// S2-06, CU-06: /trace y /audit. Filtros por consulta, explicación y fechas (RF-TRZ-03, CA-HU14.3), ninguna operación
// para modificar o borrar (RF-TRZ-02, CA-HU14.2) y verificación de la cadena desde la Api ("Hecho cuando").
[Collection(PostgresGroup.Name)]
public sealed class TraceabilityEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    /// <summary>Crea una consulta con una explicación terminada; su traza la escribe el almacén real.</summary>
    private async Task<(Guid QueryId, Guid ExplanationId)> SeedTracedExplanationAsync()
    {
        await using var db = postgres.CreateContext();
        var now = DateTimeOffset.UtcNow;
        var connection = new Connection
        {
            Id = Guid.NewGuid(),
            Platform = ConnectionPlatform.SqlServer,
            Server = $"trc-{Guid.NewGuid():N}"[..20],
            DatabaseName = "QD_Lab",
            AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
            Encryption = ConnectionEncryption.Mandatory,
            EncryptedCredentials = "no-se-usa",
            Status = ConnectionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var query = new Query { Id = Guid.NewGuid(), ConnectionId = connection.Id, QueryStoreQueryId = Random.Shared.NextInt64(1, long.MaxValue), IngestedAt = now };
        var explanation = new Explanation
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection.Id,
            QueryId = query.Id,
            State = ExplanationState.Running,
            CorrelationId = Guid.NewGuid(),
            RequestedAt = now,
        };
        db.Connections.Add(connection);
        db.Queries.Add(query);
        db.Explanations.Add(explanation);
        await db.SaveChangesAsync();

        explanation.State = ExplanationState.Completed;
        explanation.ValidationStatus = "TRAZADA";
        explanation.FindingCodesJson = "[\"F-0001\"]";
        explanation.CompletedAt = now;
        await new EfExplanationStore(db, TimeProvider.System).FinishAsync(explanation, CancellationToken.None);
        return (query.Id, explanation.Id);
    }

    [Fact]
    public async Task FiltrarPorConsultaDevuelveSoloSusEntradas()
    {
        // CA-HU14.3.
        var (queryA, explanationA) = await SeedTracedExplanationAsync();
        await SeedTracedExplanationAsync();
        using var client = await _api.ClientAsync(RoleNames.Dba);

        var entries = await client.GetFromJsonAsync<JsonElement>($"/trace?queryId={queryA}");

        var entry = Assert.Single(entries.EnumerateArray());
        Assert.Equal(explanationA, entry.GetProperty("explanation_id").GetGuid());
        Assert.Equal("TRAZADA", entry.GetProperty("validation_status").GetString());
        Assert.Equal("F-0001", entry.GetProperty("finding_codes")[0].GetString());
    }

    [Fact]
    public async Task FiltrarPorExplicacionYPorRangoDeFechas()
    {
        // RF-TRZ-03: por explicación y por rango de fechas.
        var (_, explanation) = await SeedTracedExplanationAsync();
        using var client = await _api.ClientAsync(RoleNames.Dba);
        var future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(1).ToString("O"));
        var past = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"));

        var byExplanation = await client.GetFromJsonAsync<JsonElement>($"/trace?explanationId={explanation}");
        var inRange = await client.GetFromJsonAsync<JsonElement>($"/trace?explanationId={explanation}&from={past}&to={future}");
        var afterRange = await client.GetFromJsonAsync<JsonElement>($"/trace?explanationId={explanation}&from={future}");

        Assert.Single(byExplanation.EnumerateArray());
        Assert.Single(inRange.EnumerateArray());
        Assert.Empty(afterRange.EnumerateArray());
    }

    [Fact]
    public async Task LaAuditoriaSeFiltraPorTipo()
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);

        var logins = await client.GetFromJsonAsync<JsonElement>("/audit?type=LoginSucceeded&take=500");

        Assert.NotEmpty(logins.EnumerateArray());
        Assert.All(logins.EnumerateArray(), e => Assert.Equal("LoginSucceeded", e.GetProperty("event_type").GetString()));
    }

    [Fact]
    public async Task NoExisteNingunaRutaParaModificarOBorrarLaTrazaNiLaAuditoria()
    {
        // CA-HU14.2: se revisa la tabla de rutas real de la Api.
        _ = await _api.ClientAsync(role: null);
        var routes = _api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && (raw.StartsWith("/trace", StringComparison.Ordinal) || raw.StartsWith("/audit", StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(routes);
        var methods = routes.SelectMany(r => r.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []).Distinct().ToList();
        Assert.Equal(["GET"], methods);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("POST")]
    public async Task UnaPeticionQueModificaNoTieneRuta(string method)
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/trace"));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task LaVerificacionDesdeLaApiDetectaUnaFilaAlteradaAMano()
    {
        // "Hecho cuando" de S2-06, por la Api.
        var (_, explanation) = await SeedTracedExplanationAsync();
        using var client = await _api.ClientAsync(RoleNames.Dba);
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/trace/verify")).GetProperty("intact").GetBoolean());

        long seq;
        await using (var db = postgres.CreateContext())
        {
            seq = db.TraceEntries.Single(t => t.ExplanationId == explanation).Seq;
        }

        await TamperAsync($"UPDATE audit.trace_entry SET validation_status = 'NO_VERIFICADA' WHERE seq = {seq}");
        try
        {
            var result = await client.GetFromJsonAsync<JsonElement>("/trace/verify");

            Assert.False(result.GetProperty("intact").GetBoolean());
            Assert.Equal(seq, result.GetProperty("first_broken_seq").GetInt64());
            Assert.Equal("hash_mismatch", result.GetProperty("reason").GetString());
        }
        finally
        {
            await TamperAsync($"UPDATE audit.trace_entry SET validation_status = 'TRAZADA' WHERE seq = {seq}");
        }

        Assert.True((await client.GetFromJsonAsync<JsonElement>("/trace/verify")).GetProperty("intact").GetBoolean());
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/audit/verify")).GetProperty("intact").GetBoolean());
    }

    [Theory]
    [InlineData("/trace?take=0")]
    [InlineData("/trace?take=501")]
    [InlineData("/trace?from=2026-11-15T10:00:00Z&to=2026-11-14T10:00:00Z")]
    [InlineData("/audit?type=NoExiste")]
    public async Task UnFiltroInvalidoDa400(string url)
    {
        using var client = await _api.ClientAsync(RoleNames.Dba);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnDesarrolladorNoConsultaLaTrazaNiLaAuditoria()
    {
        // DA-14 abierta: no hay rol Auditor; solo DBA (RNF-SEG-12).
        using var client = await _api.ClientAsync(RoleNames.Developer);

        using var trace = await client.GetAsync("/trace");
        using var audit = await client.GetAsync("/audit");

        Assert.Equal(HttpStatusCode.Forbidden, trace.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, audit.StatusCode);
    }

    private async Task TamperAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using (var replica = new NpgsqlCommand("SET session_replication_role = replica", connection))
        {
            await replica.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
