using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Retention;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure.Retention;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// S2-13 (RNF-SEG-10, RNF-SEG-12, RF-TRZ-05): borrado automático por vencimiento de retención, borrado bajo demanda
/// del DBA, indicador de evidencia expirada en /queries y /plans e integridad de la cadena de trazabilidad tras purgar.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class RetentionEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task PurgaAutomaticaBorraSoloLoVencidoYConservaTrazaVerificable()
    {
        var encryptor = _api.Services.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;
        var expiredAt = now.AddDays(-45);
        var freshAt = now.AddDays(-2);

        await using var db = postgres.CreateContext();
        var conn = CreateConnection(now);
        var expiredText = new QueryText
        {
            Id = Guid.NewGuid(),
            ConnectionId = conn.Id,
            QueryStoreTextId = 101,
            EncryptedSqlText = encryptor.Encrypt("SELECT 1 WHERE Secret = 'VENCIDO';"),
            IngestedAt = expiredAt,
        };
        var freshText = new QueryText
        {
            Id = Guid.NewGuid(),
            ConnectionId = conn.Id,
            QueryStoreTextId = 102,
            EncryptedSqlText = encryptor.Encrypt("SELECT 1 WHERE Secret = 'VIGENTE';"),
            IngestedAt = freshAt,
        };
        var query = new Query
        {
            Id = Guid.NewGuid(),
            ConnectionId = conn.Id,
            QueryStoreQueryId = 1,
            QueryTextId = expiredText.Id,
            IngestedAt = expiredAt,
        };
        var expiredPlan = new Plan
        {
            Id = Guid.NewGuid(),
            ConnectionId = conn.Id,
            QueryId = query.Id,
            QueryStorePlanId = 201,
            EncryptedPlanXml = encryptor.Encrypt("<ShowPlanXML />"),
            IngestedAt = expiredAt,
        };
        var freshPlan = new Plan
        {
            Id = Guid.NewGuid(),
            ConnectionId = conn.Id,
            QueryId = query.Id,
            QueryStorePlanId = 202,
            EncryptedPlanXml = encryptor.Encrypt("<ShowPlanXML />"),
            IngestedAt = freshAt,
        };

        db.Connections.Add(conn);
        db.QueryTexts.AddRange(expiredText, freshText);
        db.Queries.Add(query);
        db.Plans.AddRange(expiredPlan, freshPlan);
        await db.SaveChangesAsync();

        // Tras el merge con main, la purga es la de S2-13 (PurgeExpiredEvidence); la de #65 se quitó por duplicada.
        var purge = new PurgeExpiredEvidence(new EfEvidencePurgeStore(db), new RetentionOptions { SqlTextAndPlanDays = 30 }, TimeProvider.System);
        var outcome = await purge.ExecuteAsync();

        var counts = Assert.Single(outcome.Purged, p => p.ConnectionId == conn.Id).Counts;
        Assert.Equal(1, counts.SqlTexts);
        Assert.Equal(1, counts.Plans);

        await using var verifyDb = postgres.CreateContext();
        Assert.Null((await verifyDb.QueryTexts.AsNoTracking().SingleAsync(t => t.Id == expiredText.Id)).EncryptedSqlText);
        Assert.NotNull((await verifyDb.QueryTexts.AsNoTracking().SingleAsync(t => t.Id == freshText.Id)).EncryptedSqlText);
        Assert.Null((await verifyDb.Plans.AsNoTracking().SingleAsync(p => p.Id == expiredPlan.Id)).EncryptedPlanXml);
        Assert.NotNull((await verifyDb.Plans.AsNoTracking().SingleAsync(p => p.Id == freshPlan.Id)).EncryptedPlanXml);
        Assert.True(await verifyDb.AuditEvents.AsNoTracking().AnyAsync(e => e.EventType == AuditEventType.DataDeleted && e.UserName == PurgeExpiredEvidence.SystemActor));

        using var dba = await _api.ClientAsync(RoleNames.Dba);
        var verifyAudit = await dba.GetFromJsonAsync<JsonElement>("/audit/verify");
        Assert.True(verifyAudit.GetProperty("intact").GetBoolean());
    }

    [Fact]
    public async Task DbaBorraDatosDeConexionYApiMuestraEvidenciaExpiradaSinRomperAuditoria()
    {
        var encryptor = _api.Services.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;

        await using var db = postgres.CreateContext();
        var conn = CreateConnection(now);
        var sqlText = new QueryText
        {
            Id = Guid.NewGuid(),
            ConnectionId = conn.Id,
            QueryStoreTextId = 1,
            EncryptedSqlText = encryptor.Encrypt("SELECT * FROM dbo.Orders WHERE Code = 'ABC';"),
            IngestedAt = now,
        };
        var query = new Query
        {
            Id = Guid.NewGuid(),
            ConnectionId = conn.Id,
            QueryStoreQueryId = 1,
            QueryTextId = sqlText.Id,
            IngestedAt = now,
        };
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            ConnectionId = conn.Id,
            QueryId = query.Id,
            QueryStorePlanId = 1,
            EncryptedPlanXml = encryptor.Encrypt("<ShowPlanXML />"),
            IngestedAt = now,
        };

        db.Connections.Add(conn);
        db.QueryTexts.Add(sqlText);
        db.Queries.Add(query);
        db.Plans.Add(plan);
        await db.SaveChangesAsync();

        using var developer = await _api.ClientAsync(RoleNames.Developer);
        using var forbidden = await developer.DeleteAsync($"/connections/{conn.Id}/data");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var dba = await _api.ClientAsync(RoleNames.Dba);
        using var deleted = await dba.DeleteAsync($"/connections/{conn.Id}/data");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        var body = await deleted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("textsCleared").GetInt32());
        Assert.Equal(1, body.GetProperty("plansCleared").GetInt32());

        using var detailResponse = await dba.GetAsync($"/queries/{query.Id}?window=24h");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(detail.GetProperty("sqlExpired").GetBoolean());

        using var planResponse = await dba.GetAsync($"/queries/{query.Id}/plans/{plan.Id}");
        Assert.Equal(HttpStatusCode.OK, planResponse.StatusCode);
        var planView = await planResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(planView.GetProperty("planExpired").GetBoolean());

        var verifyAudit = await dba.GetFromJsonAsync<JsonElement>("/audit/verify");
        Assert.True(verifyAudit.GetProperty("intact").GetBoolean());

        using var missing = await dba.DeleteAsync($"/connections/{Guid.NewGuid()}/data");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static Connection CreateConnection(DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Platform = ConnectionPlatform.SqlServer,
        Server = $"ret-{Guid.NewGuid():N}"[..20],
        DatabaseName = "QD_Lab",
        AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
        Encryption = ConnectionEncryption.Mandatory,
        EncryptedCredentials = "no-se-usa",
        Status = ConnectionStatus.Active,
        CreatedAt = now,
        UpdatedAt = now,
    };
}
