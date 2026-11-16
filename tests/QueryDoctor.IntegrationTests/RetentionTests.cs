using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Retention;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Explanations;
using QueryDoctor.Infrastructure.Retention;
using QueryDoctor.Infrastructure.Scoring;
using QueryDoctor.Infrastructure.Traceability;

namespace QueryDoctor.IntegrationTests;

/// <summary>
/// S2-13 (RNF-SEG-10), "Hecho cuando": lo vencido desaparece; la Api dice "evidencia expirada"; el borrado a pedido es
/// solo DBA y queda auditado en la misma transacción; la traza y la auditoría siguen verificando después de la purga.
/// </summary>
[Collection(PostgresGroup.Name)]
public sealed class RetentionTests(PostgresFixture postgres) : IDisposable
{
    private static readonly string WorkerMasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly ApiFactory _api = new(postgres);

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task ElTrabajoRetentionPurgaLoVencidoDejaLoVigenteYLaTrazaSigueVerificando()
    {
        var old = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-40));
        var fresh = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-1));
        var runtimeStatsBefore = await RuntimeStatsAsync(old.ConnectionId);

        // Con la misma DI que QueryDoctor.Worker/Program.cs (acceso Sistema, plazo por configuración).
        await using (var worker = Worker(days: 30))
        {
            using var scope = worker.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(CancellationToken.None);
        }

        await using (var db = postgres.CreateContext())
        {
            var oldText = await db.QueryTexts.SingleAsync(t => t.Id == old.TextId);
            Assert.Null(oldText.EncryptedSqlText);
            Assert.NotNull(oldText.PurgedAt);
            var oldPlan = await db.Plans.SingleAsync(p => p.Id == old.PlanId);
            Assert.Null(oldPlan.EncryptedPlanXml);
            Assert.NotNull(oldPlan.PurgedAt);
            var oldFinding = await db.Findings.SingleAsync(f => f.Id == old.FindingId);
            Assert.Equal(string.Empty, oldFinding.EncryptedEvidenceJson);
            Assert.Equal("REG-01", oldFinding.RuleId);

            Assert.NotNull((await db.QueryTexts.SingleAsync(t => t.Id == fresh.TextId)).EncryptedSqlText);
            Assert.Null((await db.Plans.SingleAsync(p => p.Id == fresh.PlanId)).PurgedAt);
            Assert.NotEqual(string.Empty, (await db.Findings.SingleAsync(f => f.Id == fresh.FindingId)).EncryptedEvidenceJson);

            // D8: la purga automática se audita por conexión, sin usuario. Lo vigente no deja evento.
            var events = await DataDeletedEventsAsync(db, old.ConnectionId);
            var purgeEvent = Assert.Single(events);
            Assert.Null(purgeEvent.UserId);
            Assert.Equal(PurgeExpiredEvidence.SystemActor, purgeEvent.UserName);
            Assert.Equal(AuditEventDraft.OutcomeOk, purgeEvent.Outcome);
            Assert.Empty(await DataDeletedEventsAsync(db, fresh.ConnectionId));

            // La traza de la explicación sigue ahí (sin FK hacia lo purgado).
            Assert.True(await db.TraceEntries.AnyAsync(t => t.QueryId == old.QueryId));
        }

        Assert.Equal(runtimeStatsBefore, await RuntimeStatsAsync(old.ConnectionId));

        using var dba = await _api.ClientAsync(RoleNames.Dba);
        Assert.True((await dba.GetFromJsonAsync<JsonElement>("/trace/verify")).GetProperty("intact").GetBoolean());
        Assert.True((await dba.GetFromJsonAsync<JsonElement>("/audit/verify")).GetProperty("intact").GetBoolean());

        // Una segunda corrida no encuentra nada ni deja otro evento.
        await using (var worker = Worker(days: 30))
        {
            using var scope = worker.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(CancellationToken.None);
        }

        await using (var db = postgres.CreateContext())
        {
            Assert.Single(await DataDeletedEventsAsync(db, old.ConnectionId));
        }
    }

    [Fact]
    public async Task LaApiExponeEvidenciaExpiradaEnListadoDetallePlanYExplicacion()
    {
        var old = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-40));
        var fresh = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-1));
        await using (var worker = Worker(days: 30))
        {
            using var scope = worker.CreateScope();
            await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(CancellationToken.None);
        }

        using var dba = await _api.ClientAsync(RoleNames.Dba);

        var list = await dba.GetFromJsonAsync<JsonElement>($"/queries?connectionId={old.ConnectionId}&window=30d");
        var item = Assert.Single(list.EnumerateArray());
        Assert.True(item.GetProperty("sqlExpired").GetBoolean());
        Assert.Equal("[Evidencia expirada]", item.GetProperty("sqlPreview").GetString());

        var detail = await dba.GetFromJsonAsync<JsonElement>($"/queries/{old.QueryId}");
        Assert.True(detail.GetProperty("sqlExpired").GetBoolean());
        Assert.Equal(JsonValueKind.String, detail.GetProperty("sqlExpiredAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("sqlText").ValueKind);
        var plan = Assert.Single(detail.GetProperty("plans").EnumerateArray());
        Assert.True(plan.GetProperty("planExpired").GetBoolean());
        Assert.Equal(JsonValueKind.String, plan.GetProperty("expiredAt").ValueKind);
        var finding = Assert.Single(detail.GetProperty("findings").EnumerateArray());
        Assert.True(finding.GetProperty("evidenceExpired").GetBoolean());
        Assert.Empty(finding.GetProperty("evidence").EnumerateObject());
        Assert.Equal("REG-01", finding.GetProperty("ruleId").GetString());

        var tree = await dba.GetFromJsonAsync<JsonElement>($"/queries/{old.QueryId}/plans/{old.PlanId}");
        Assert.True(tree.GetProperty("planExpired").GetBoolean());
        Assert.Equal(JsonValueKind.String, tree.GetProperty("planExpiredAt").ValueKind);
        Assert.Empty(tree.GetProperty("statements").EnumerateArray());

        // Lo vigente sigue con su texto y su evidencia.
        var freshDetail = await dba.GetFromJsonAsync<JsonElement>($"/queries/{fresh.QueryId}");
        Assert.False(freshDetail.GetProperty("sqlExpired").GetBoolean());
        Assert.False(Assert.Single(freshDetail.GetProperty("plans").EnumerateArray()).GetProperty("planExpired").GetBoolean());
        Assert.False(Assert.Single(freshDetail.GetProperty("findings").EnumerateArray()).GetProperty("evidenceExpired").GetBoolean());

        // D6: el Worker lee la evidencia como expirada y no llamará a la IA (ExplainQueryTests cubre el resto).
        await using var db = postgres.CreateContext();
        var source = await new EfExplanationStore(db, TimeProvider.System).LoadAsync(old.QueryId, CancellationToken.None);
        Assert.NotNull(source);
        Assert.True(source.EvidenceExpired);
        Assert.Null(source.EncryptedSqlText);
        Assert.Null(source.EncryptedPlanXml);
    }

    [Fact]
    public async Task UnPlanTooLargeSinXmlNoApareceComoExpirado()
    {
        var seeded = await SeedAsync(DateTimeOffset.UtcNow, tooLargePlan: true);
        using var dba = await _api.ClientAsync(RoleNames.Dba);

        var tree = await dba.GetFromJsonAsync<JsonElement>($"/queries/{seeded.QueryId}/plans/{seeded.PlanId}");

        Assert.False(tree.GetProperty("planExpired").GetBoolean());
        Assert.Equal("TooLarge", tree.GetProperty("notAnalyzableCause").GetString());
    }

    [Fact]
    public async Task ElBorradoAPedidoDeUnaConsultaEsDelDbaQuedaAuditadoYEsIdempotente()
    {
        var seeded = await SeedAsync(DateTimeOffset.UtcNow);
        var (dba, dbaId) = await _api.SessionAsync([RoleNames.Dba]);
        using var _ = dba;

        using (var deleted = await dba.DeleteAsync($"/queries/{seeded.QueryId}/evidence"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        await using (var db = postgres.CreateContext())
        {
            Assert.NotNull((await db.QueryTexts.SingleAsync(t => t.Id == seeded.TextId)).PurgedAt);
            Assert.NotNull((await db.Plans.SingleAsync(p => p.Id == seeded.PlanId)).PurgedAt);
            Assert.Equal(string.Empty, (await db.Findings.SingleAsync(f => f.Id == seeded.FindingId)).EncryptedEvidenceJson);
            // No borra la consulta ni la conexión: solo su evidencia.
            Assert.True(await db.Queries.AnyAsync(q => q.Id == seeded.QueryId));
            Assert.True(await db.Connections.AnyAsync(c => c.Id == seeded.ConnectionId));
        }

        using (var again = await dba.DeleteAsync($"/queries/{seeded.QueryId}/evidence"))
        {
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        }

        await using (var db = postgres.CreateContext())
        {
            var events = await db.AuditEvents.AsNoTracking()
                .Where(e => e.EventType == AuditEventType.DataDeleted && e.SubjectType == AuditEventDraft.SubjectQuery && e.SubjectId == seeded.QueryId)
                .OrderBy(e => e.Seq)
                .ToListAsync();
            Assert.Equal([AuditEventDraft.OutcomeOk, AuditEventDraft.OutcomeNothingToDelete], events.Select(e => e.Outcome));
            Assert.All(events, e => Assert.Equal(dbaId, e.UserId));
        }

        using (var missing = await dba.DeleteAsync($"/queries/{Guid.NewGuid()}/evidence"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        Assert.True((await dba.GetFromJsonAsync<JsonElement>("/audit/verify")).GetProperty("intact").GetBoolean());
        Assert.True((await dba.GetFromJsonAsync<JsonElement>("/trace/verify")).GetProperty("intact").GetBoolean());
    }

    [Fact]
    public async Task ElBorradoAPedidoDeUnaConexionPurgaTodasSusConsultas()
    {
        var first = await SeedAsync(DateTimeOffset.UtcNow);
        var second = await SeedAsync(DateTimeOffset.UtcNow, connectionId: first.ConnectionId);
        using var dba = await _api.ClientAsync(RoleNames.Dba);

        using var deleted = await dba.DeleteAsync($"/connections/{first.ConnectionId}/evidence");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var db = postgres.CreateContext();
        Assert.Equal(2, await db.Plans.CountAsync(p => p.ConnectionId == first.ConnectionId && p.PurgedAt != null));
        Assert.Equal(2, await db.QueryTexts.CountAsync(t => t.ConnectionId == first.ConnectionId && t.PurgedAt != null));
        Assert.Equal(new[] { first.QueryId, second.QueryId }.Order(), (await db.Findings
            .Where(f => f.ConnectionId == first.ConnectionId && f.EncryptedEvidenceJson == string.Empty)
            .Select(f => f.QueryId)
            .ToListAsync()).Order());
        var audit = Assert.Single(await DataDeletedEventsAsync(db, first.ConnectionId));
        Assert.Equal(AuditEventDraft.OutcomeOk, audit.Outcome);
        Assert.NotNull(audit.UserId);
    }

    [Fact]
    public async Task SinSerDbaNoSeBorraNada()
    {
        var seeded = await SeedAsync(DateTimeOffset.UtcNow);
        var (developer, developerId) = await _api.SessionAsync([RoleNames.Developer]);
        using var _ = developer;
        await using (var db = postgres.CreateContext())
        {
            // Aun con la conexión asignada (S2-12), borrar es solo del DBA.
            db.UserConnections.Add(new UserConnection { UserId = developerId, ConnectionId = seeded.ConnectionId, AssignedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/queries/{seeded.QueryId}/evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/connections/{seeded.ConnectionId}/evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.DeleteAsync($"/queries/{seeded.QueryId}/evidence")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await developer.DeleteAsync($"/connections/{seeded.ConnectionId}/evidence")).StatusCode);

        // CSRF: UseSameOriginForms cubre DELETE.
        using var dba = await _api.ClientAsync(RoleNames.Dba);
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Delete, $"/queries/{seeded.QueryId}/evidence");
        crossOrigin.Headers.Remove("Origin");
        crossOrigin.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await dba.SendAsync(crossOrigin)).StatusCode);

        await using var check = postgres.CreateContext();
        Assert.Null((await check.Plans.SingleAsync(p => p.Id == seeded.PlanId)).PurgedAt);
        Assert.Null((await check.QueryTexts.SingleAsync(t => t.Id == seeded.TextId)).PurgedAt);
        Assert.Empty(await DataDeletedEventsAsync(check, seeded.ConnectionId));
        Assert.False(await check.AuditEvents.AnyAsync(e => e.EventType == AuditEventType.DataDeleted && e.SubjectId == seeded.QueryId));
    }

    [Fact]
    public async Task SiLaAuditoriaFallaNoSeBorraNadaYElReintentoDejaAmbos()
    {
        // RF-TRZ-05: otra sesión retiene el bloqueo de la cadena de auditoría y la escritura del evento se cancela.
        var seeded = await SeedAsync(DateTimeOffset.UtcNow);
        var actor = new EvidenceDeletionActor(Guid.NewGuid(), "dba");

        await using (await HoldAuditLockAsync())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await using var db = postgres.CreateContext();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => new DeleteEvidence(new EfEvidencePurgeStore(db), TimeProvider.System)
                    .ExecuteAsync(EvidenceScope.ForQuery(seeded.QueryId), actor, timeout.Token));
        }

        await using (var db = postgres.CreateContext())
        {
            Assert.Null((await db.Plans.SingleAsync(p => p.Id == seeded.PlanId)).PurgedAt);
            Assert.NotNull((await db.QueryTexts.SingleAsync(t => t.Id == seeded.TextId)).EncryptedSqlText);
            Assert.NotEqual(string.Empty, (await db.Findings.SingleAsync(f => f.Id == seeded.FindingId)).EncryptedEvidenceJson);
            Assert.False(await db.AuditEvents.AnyAsync(e => e.SubjectId == seeded.QueryId));
        }

        await using (var db = postgres.CreateContext())
        {
            var retried = await new DeleteEvidence(new EfEvidencePurgeStore(db), TimeProvider.System)
                .ExecuteAsync(EvidenceScope.ForQuery(seeded.QueryId), actor);
            var counts = Assert.IsType<DeleteEvidenceResult.Deleted>(retried).Counts;
            Assert.Equal(new EvidencePurgeCounts(1, 1, 1), counts);
        }

        await using (var db = postgres.CreateContext())
        {
            Assert.NotNull((await db.Plans.SingleAsync(p => p.Id == seeded.PlanId)).PurgedAt);
            Assert.Single(await db.AuditEvents.Where(e => e.SubjectId == seeded.QueryId).ToListAsync());
        }
    }

    [Fact]
    public async Task ElCorteDelPlazoSeAplicaDentroDeUnaMismaConexion()
    {
        // Auditoría B2: lo viejo y lo vigente en la misma conexión; el corte lo hace el SQL que genera EF.
        var old = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-40));
        var fresh = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-1), connectionId: old.ConnectionId);

        await RunRetentionAsync(days: 30);

        await using var db = postgres.CreateContext();
        Assert.NotNull((await db.Plans.SingleAsync(p => p.Id == old.PlanId)).PurgedAt);
        Assert.NotNull((await db.QueryTexts.SingleAsync(t => t.Id == old.TextId)).PurgedAt);
        Assert.Equal(string.Empty, (await db.Findings.SingleAsync(f => f.Id == old.FindingId)).EncryptedEvidenceJson);
        Assert.Null((await db.Plans.SingleAsync(p => p.Id == fresh.PlanId)).PurgedAt);
        Assert.NotNull((await db.QueryTexts.SingleAsync(t => t.Id == fresh.TextId)).EncryptedSqlText);
        Assert.NotEqual(string.Empty, (await db.Findings.SingleAsync(f => f.Id == fresh.FindingId)).EncryptedEvidenceJson);
        Assert.Single(await DataDeletedEventsAsync(db, old.ConnectionId));
    }

    [Fact]
    public async Task LaPurgaRecogeEvidenciaQueQuedoEnUnPlanYaPurgado()
    {
        // Auditoría M1: un recálculo que confirmó después de la purga dejó evidencia en un plan purgado. La conexión no tiene
        // nada más vencido, y aun así la corrida siguiente debe encontrarla, vaciarla y auditarlo.
        var seeded = await SeedAsync(DateTimeOffset.UtcNow);
        await using (var db = postgres.CreateContext())
        {
            await new DeleteEvidence(new EfEvidencePurgeStore(db), TimeProvider.System)
                .ExecuteAsync(EvidenceScope.ForQuery(seeded.QueryId), new EvidenceDeletionActor(Guid.NewGuid(), "dba"));
            await db.Findings.Where(f => f.Id == seeded.FindingId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.EncryptedEvidenceJson, "enc:residuo-con-literales"));
        }

        await RunRetentionAsync(days: 30);

        await using var check = postgres.CreateContext();
        Assert.Equal(string.Empty, (await check.Findings.SingleAsync(f => f.Id == seeded.FindingId)).EncryptedEvidenceJson);
        var purgeEvent = Assert.Single(await DataDeletedEventsAsync(check, seeded.ConnectionId));
        Assert.Equal(PurgeExpiredEvidence.SystemActor, purgeEvent.UserName);
    }

    [Fact]
    public async Task UnRecalculoQueConfirmaDespuesDeLaPurgaNoEscribeEvidencia()
    {
        // Auditoría M1 (b): la instantánea del recálculo se tomó antes de la purga y el guardado llega después.
        var seeded = await SeedAsync(DateTimeOffset.UtcNow);
        await using (var db = postgres.CreateContext())
        {
            await new DeleteEvidence(new EfEvidencePurgeStore(db), TimeProvider.System)
                .ExecuteAsync(EvidenceScope.ForQuery(seeded.QueryId), new EvidenceDeletionActor(Guid.NewGuid(), "dba"));
        }

        var now = DateTimeOffset.UtcNow;
        var lateRun = new AnalysisRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = seeded.ConnectionId,
            QueryId = seeded.QueryId,
            PlanId = seeded.PlanId,
            CatalogVersion = "1.0.0",
            Status = AnalysisRunStatus.Succeeded,
            FindingsCount = 1,
            StartedAt = now,
            FinishedAt = now,
        };
        var lateFinding = new Finding
        {
            Id = Guid.NewGuid(),
            AnalysisRunId = lateRun.Id,
            ConnectionId = seeded.ConnectionId,
            QueryId = seeded.QueryId,
            PlanId = seeded.PlanId,
            FindingCode = $"F-{Guid.NewGuid():N}"[..14],
            RuleId = "REG-01",
            RuleVersion = "1.0.0",
            CatalogVersion = "1.0.0",
            SchemaVersion = "1.0.0",
            Severity = FindingSeverity.Alta,
            EvidenceClass = EvidenceClass.Estimada,
            EncryptedEvidenceJson = "enc:evidencia-tardia-con-literales",
            AnalyzedAt = now,
        };
        var batch = new ConnectionScoringPersistenceBatch(
            seeded.ConnectionId,
            IngestionRunId: null,
            new ThresholdSet { Id = Guid.NewGuid(), CatalogVersion = "1.0.0", ConfigurationHash = Guid.NewGuid().ToString("N"), CreatedAt = now },
            [new AnalyzedPlanPersistenceItem(seeded.PlanId, PlanAnalysisStatus.Analyzable, lateRun, [lateFinding])],
            []);

        await using (var db = postgres.CreateContext())
        {
            await new EfScoreRecalculationStore(db).SaveBatchAsync(batch);
        }

        await using var check = postgres.CreateContext();
        Assert.False(await check.AnalysisRuns.AnyAsync(r => r.Id == lateRun.Id));
        Assert.False(await check.Findings.AnyAsync(f => f.Id == lateFinding.Id));
        Assert.False(await check.Findings.AnyAsync(f => f.PlanId == seeded.PlanId && f.EncryptedEvidenceJson != string.Empty));
    }

    [Fact]
    public async Task PurgarNoMarcaUnPlanTooLargeNiUnTextoQueNuncaLlego()
    {
        // Auditoría B1: no hay contenido que vencer; después de la purga y del borrado a pedido siguen sin salir "expirados".
        var seeded = await SeedAsync(DateTimeOffset.UtcNow.AddDays(-40), tooLargePlan: true, withoutSqlText: true);
        using var dba = await _api.ClientAsync(RoleNames.Dba);

        await RunRetentionAsync(days: 30);
        using (var deleted = await dba.DeleteAsync($"/queries/{seeded.QueryId}/evidence"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        var tree = await dba.GetFromJsonAsync<JsonElement>($"/queries/{seeded.QueryId}/plans/{seeded.PlanId}");
        Assert.False(tree.GetProperty("planExpired").GetBoolean());
        Assert.Equal("TooLarge", tree.GetProperty("notAnalyzableCause").GetString());
        var detail = await dba.GetFromJsonAsync<JsonElement>($"/queries/{seeded.QueryId}");
        Assert.False(detail.GetProperty("sqlExpired").GetBoolean());

        await using var db = postgres.CreateContext();
        Assert.Null((await db.Plans.SingleAsync(p => p.Id == seeded.PlanId)).PurgedAt);
        Assert.Null((await db.QueryTexts.SingleAsync(t => t.Id == seeded.TextId)).PurgedAt);
        Assert.Empty(await DataDeletedEventsAsync(db, seeded.ConnectionId));
        var onDemand = await db.AuditEvents.SingleAsync(e => e.EventType == AuditEventType.DataDeleted && e.SubjectId == seeded.QueryId);
        Assert.Equal(AuditEventDraft.OutcomeNothingToDelete, onDemand.Outcome);
    }

    private async Task RunRetentionAsync(int days)
    {
        await using var worker = Worker(days);
        using var scope = worker.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(CancellationToken.None);
    }

    private sealed record Seeded(Guid ConnectionId, Guid QueryId, Guid TextId, Guid PlanId, Guid FindingId);

    /// <summary>
    /// Una consulta con texto, plan, estadística, análisis con un hallazgo y una explicación terminada (su traza la
    /// escribe el almacén real). Todo cifrado con la clave de la Api, para leerlo después por HTTP.
    /// </summary>
    private async Task<Seeded> SeedAsync(DateTimeOffset ingestedAt, Guid? connectionId = null, bool tooLargePlan = false, bool withoutSqlText = false)
    {
        var encryptor = _api.Services.GetRequiredService<IFieldEncryptor>();
        var now = DateTimeOffset.UtcNow;
        await using var db = postgres.CreateContext();

        var connection = connectionId ?? Guid.NewGuid();
        if (connectionId is null)
        {
            db.Connections.Add(new Connection
            {
                Id = connection,
                Platform = ConnectionPlatform.SqlServer,
                Server = $"ret-{Guid.NewGuid():N}"[..20],
                DatabaseName = "QD_Lab",
                AuthenticationMethod = AuthenticationMethod.SqlAuthentication,
                Encryption = ConnectionEncryption.Mandatory,
                EncryptedCredentials = "no-se-usa",
                Status = ConnectionStatus.Active,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        var text = new QueryText
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection,
            QueryStoreTextId = Random.Shared.NextInt64(1, long.MaxValue),
            EncryptedSqlText = withoutSqlText ? null : encryptor.Encrypt("SELECT Id FROM dbo.Pedidos WHERE Documento = '10203040';"),
            IngestedAt = ingestedAt,
        };
        var query = new Query
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection,
            QueryStoreQueryId = Random.Shared.NextInt64(1, long.MaxValue),
            QueryTextId = text.Id,
            IngestedAt = ingestedAt,
        };
        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection,
            QueryId = query.Id,
            QueryStorePlanId = Random.Shared.NextInt64(1, long.MaxValue),
            EncryptedPlanXml = tooLargePlan ? null : encryptor.Encrypt(PlanXml),
            EngineVersion = "16.0.1000.6",
            AnalysisStatus = tooLargePlan ? PlanAnalysisStatus.NotAnalyzable : PlanAnalysisStatus.Analyzable,
            NotAnalyzableCause = tooLargePlan ? "TooLarge" : null,
            IngestedAt = ingestedAt,
        };
        var stat = new RuntimeStat
        {
            ConnectionId = connection,
            PlanId = plan.Id,
            ExecutionType = ExecutionType.Regular,
            IntervalId = 1,
            IntervalStart = ingestedAt.AddHours(-1),
            IntervalEnd = ingestedAt,
            ExecutionCount = 10,
            AvgDurationMicroseconds = 1000,
            AvgCpuTimeMicroseconds = 500,
            AvgLogicalIoReads = 100,
            IngestedAt = ingestedAt,
        };
        var thresholdSet = new ThresholdSet
        {
            Id = Guid.NewGuid(),
            CatalogVersion = "1.0.0",
            ConfigurationHash = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
        };
        var run = new AnalysisRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection,
            QueryId = query.Id,
            PlanId = plan.Id,
            ThresholdSetId = thresholdSet.Id,
            CatalogVersion = "1.0.0",
            Status = AnalysisRunStatus.Succeeded,
            FindingsCount = 1,
            StartedAt = ingestedAt,
            FinishedAt = ingestedAt,
        };
        var finding = new Finding
        {
            Id = Guid.NewGuid(),
            AnalysisRunId = run.Id,
            ConnectionId = connection,
            QueryId = query.Id,
            PlanId = plan.Id,
            FindingCode = $"F-{Guid.NewGuid():N}"[..14],
            RuleId = "REG-01",
            RuleVersion = "1.0.0",
            CatalogVersion = "1.0.0",
            SchemaVersion = "1.0.0",
            Kind = FindingKind.Senal,
            Severity = FindingSeverity.Alta,
            EvidenceClass = EvidenceClass.Estimada,
            NodesJson = "[{\"node_id\":4,\"physical_op\":\"Clustered Index Scan\"}]",
            EncryptedEvidenceJson = encryptor.Encrypt("{\"expression\":\"CONVERT(varchar(20), Documento) = '10203040'\",\"convert_issue\":\"ImplicitConvert\"}"),
            AnalyzedAt = ingestedAt,
        };
        var explanation = new Explanation
        {
            Id = Guid.NewGuid(),
            ConnectionId = connection,
            QueryId = query.Id,
            State = ExplanationState.Running,
            CorrelationId = Guid.NewGuid(),
            RequestedAt = now,
        };

        db.QueryTexts.Add(text);
        db.Queries.Add(query);
        db.Plans.Add(plan);
        db.RuntimeStats.Add(stat);
        db.ThresholdSets.Add(thresholdSet);
        db.AnalysisRuns.Add(run);
        db.Findings.Add(finding);
        db.Explanations.Add(explanation);
        await db.SaveChangesAsync();

        explanation.State = ExplanationState.Completed;
        explanation.ValidationStatus = "TRAZADA";
        explanation.FindingCodesJson = $"[\"{finding.FindingCode}\"]";
        explanation.CompletedAt = now;
        await new EfExplanationStore(db, TimeProvider.System).FinishAsync(explanation, CancellationToken.None);

        return new Seeded(connection, query.Id, text.Id, plan.Id, finding.Id);
    }

    private const string PlanXml =
        "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\" Version=\"1.564\" Build=\"16.0.1000.6\">" +
        "<BatchSequence><Batch><Statements>" +
        "<StmtSimple StatementId=\"1\" StatementType=\"SELECT\" StatementSubTreeCost=\"3.2\" StatementEstRows=\"100\" StatementText=\"SELECT Id FROM dbo.Pedidos WHERE Documento = '10203040'\">" +
        "<QueryPlan><RelOp NodeId=\"4\" PhysicalOp=\"Clustered Index Scan\" LogicalOp=\"Clustered Index Scan\" EstimateRows=\"5000\" EstimateIO=\"2.8\" EstimateCPU=\"0.4\" AvgRowSize=\"32\" EstimatedTotalSubtreeCost=\"3.2\" Parallel=\"0\" EstimateRebinds=\"0\" EstimateRewinds=\"0\">" +
        "<IndexScan><Object Database=\"[QD_Lab]\" Schema=\"[dbo]\" Table=\"[Pedidos]\" Index=\"[PK_Pedidos]\" />" +
        "<Predicate><ScalarOperator ScalarString=\"Documento = '10203040'\" /></Predicate></IndexScan>" +
        "</RelOp></QueryPlan></StmtSimple></Statements></Batch></BatchSequence></ShowPlanXML>";

    /// <summary>Composición del Worker para retention, como en <c>QueryDoctor.Worker/Program.cs</c>.</summary>
    private ServiceProvider Worker(int days)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:QueryDoctor"] = postgres.ConnectionString,
            ["Security:SecretProtector:MasterKey"] = WorkerMasterKey,
            ["Retention:SqlTextAndPlanDays"] = days.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .AddSystemAccessScope()
            .AddRetention(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private async Task<long> RuntimeStatsAsync(Guid connectionId)
    {
        await using var db = postgres.CreateContext();
        return await db.RuntimeStats.LongCountAsync(r => r.ConnectionId == connectionId);
    }

    private static Task<List<AuditEvent>> DataDeletedEventsAsync(Infrastructure.Persistence.AppDbContext db, Guid connectionId) =>
        db.AuditEvents.AsNoTracking()
            .Where(e => e.EventType == AuditEventType.DataDeleted
                && e.SubjectType == AuditEventDraft.SubjectConnection
                && e.SubjectId == connectionId)
            .OrderBy(e => e.Seq)
            .ToListAsync();

    private async Task<AuditLockHold> HoldAuditLockAsync()
    {
        // Sin pool: un bloqueo de sesión no debe sobrevivir en una conexión reciclada.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await AuditLockHold.RunAsync(connection, "SELECT pg_advisory_lock(@key)");
        return new AuditLockHold(connection);
    }

    private sealed class AuditLockHold(NpgsqlConnection connection) : IAsyncDisposable
    {
        public static async Task RunAsync(NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("key", ChainWriter.AuditLockKey);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await RunAsync(connection, "SELECT pg_advisory_unlock(@key)");
            await connection.DisposeAsync();
        }
    }
}
