using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Retention;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Infrastructure;
using QueryDoctor.UnitTests.Connections;

namespace QueryDoctor.UnitTests.Retention;

/// <summary>
/// Retención (S2-13, RNF-SEG-10): plazo configurable y validado, corte por fecha de ingesta, auditoría de la purga
/// automática (sin usuario) y del borrado a pedido (con usuario, también si no quedaba nada).
/// </summary>
public sealed class EvidenceRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 3, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3651)]
    public void UnPlazoFueraDeRangoNoSeAcepta(int days)
    {
        var options = new RetentionOptions { SqlTextAndPlanDays = days };
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Fact]
    public void ElPlazoPorDefectoEsDe30Dias()
    {
        var options = new RetentionOptions();
        options.Validate();
        Assert.Equal(30, options.SqlTextAndPlanDays);
    }

    [Fact]
    public void ElWorkerNoArrancaConUnPlazoFueraDeRango()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:QueryDoctor"] = "Host=localhost;Database=x;Username=x;Password=x",
                ["Retention:SqlTextAndPlanDays"] = "0",
            })
            .Build();
        services.AddRetention(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => DependencyInjection.ScheduleRetention(provider));
    }

    [Fact]
    public async Task LaPurgaAutomaticaCortaPorElPlazoYAuditaCadaConexionSinUsuario()
    {
        var withData = Guid.NewGuid();
        var alreadyEmpty = Guid.NewGuid();
        var store = new RecordingPurgeStore
        {
            Expired = [withData, alreadyEmpty],
            CountsFor = id => id == withData ? new EvidencePurgeCounts(2, 3, 4) : EvidencePurgeCounts.None,
        };
        var sut = new PurgeExpiredEvidence(store, new RetentionOptions { SqlTextAndPlanDays = 7 }, new FixedClock(Now));

        var outcome = await sut.ExecuteAsync();

        Assert.Equal(Now.AddDays(-7), outcome.IngestedBefore);
        Assert.Equal(Now.AddDays(-7), store.LastIngestedBefore);
        var (connectionId, counts) = Assert.Single(outcome.Purged);
        Assert.Equal(withData, connectionId);
        Assert.Equal(9, counts.Total);

        // Una conexión sin filas que purgar no deja evento.
        var audit = Assert.Single(store.Audits);
        Assert.Equal(AuditEventType.DataDeleted, audit.EventType);
        Assert.Null(audit.UserId);
        Assert.Equal(PurgeExpiredEvidence.SystemActor, audit.UserName);
        Assert.Equal(AuditEventDraft.SubjectConnection, audit.SubjectType);
        Assert.Equal(withData, audit.SubjectId);
        Assert.Equal(AuditEventDraft.OutcomeOk, audit.Outcome);
    }

    [Fact]
    public async Task ElBorradoAPedidoAuditaAlUsuarioYNoUsaElPlazo()
    {
        var queryId = Guid.NewGuid();
        var dbaId = Guid.NewGuid();
        var store = new RecordingPurgeStore { CountsFor = _ => new EvidencePurgeCounts(1, 1, 2) };
        var sut = new DeleteEvidence(store, new FixedClock(Now));

        var result = await sut.ExecuteAsync(EvidenceScope.ForQuery(queryId), new EvidenceDeletionActor(dbaId, "dba"));

        Assert.IsType<DeleteEvidenceResult.Deleted>(result);
        Assert.True(store.PurgedWithoutCutoff);
        var audit = Assert.Single(store.Audits);
        Assert.Equal(AuditEventType.DataDeleted, audit.EventType);
        Assert.Equal(dbaId, audit.UserId);
        Assert.Equal("dba", audit.UserName);
        Assert.Equal(AuditEventDraft.SubjectQuery, audit.SubjectType);
        Assert.Equal(queryId, audit.SubjectId);
        Assert.Equal(AuditEventDraft.OutcomeOk, audit.Outcome);
    }

    [Fact]
    public async Task RepetirElBorradoDejaOtroEventoConNadaQueBorrar()
    {
        var store = new RecordingPurgeStore { CountsFor = _ => EvidencePurgeCounts.None };
        var sut = new DeleteEvidence(store, new FixedClock(Now));

        var result = await sut.ExecuteAsync(EvidenceScope.ForConnection(Guid.NewGuid()), new EvidenceDeletionActor(Guid.NewGuid(), "dba"));

        Assert.IsType<DeleteEvidenceResult.Deleted>(result);
        Assert.Equal(AuditEventDraft.OutcomeNothingToDelete, Assert.Single(store.Audits).Outcome);
    }

    [Fact]
    public async Task SiNoExisteNoSeBorraNiSeAudita()
    {
        var store = new RecordingPurgeStore { Exists = false };
        var sut = new DeleteEvidence(store, new FixedClock(Now));

        var result = await sut.ExecuteAsync(EvidenceScope.ForQuery(Guid.NewGuid()), new EvidenceDeletionActor(Guid.NewGuid(), "dba"));

        Assert.IsType<DeleteEvidenceResult.NotFound>(result);
        Assert.Equal(0, store.PurgeCalls);
        Assert.Empty(store.Audits);
    }

    private sealed class RecordingPurgeStore : IEvidencePurgeStore
    {
        public bool Exists { get; init; } = true;

        public IReadOnlyList<Guid> Expired { get; init; } = [];

        public Func<Guid, EvidencePurgeCounts> CountsFor { get; init; } = _ => EvidencePurgeCounts.None;

        public List<AuditEventDraft> Audits { get; } = [];

        public DateTimeOffset? LastIngestedBefore { get; private set; }

        public bool PurgedWithoutCutoff { get; private set; }

        public int PurgeCalls { get; private set; }

        public Task<bool> ExistsAsync(EvidenceScope scope, CancellationToken cancellationToken) => Task.FromResult(Exists);

        public Task<IReadOnlyList<Guid>> ConnectionsWithExpiredEvidenceAsync(DateTimeOffset ingestedBefore, CancellationToken cancellationToken)
        {
            LastIngestedBefore = ingestedBefore;
            return Task.FromResult(Expired);
        }

        public Task<EvidencePurgeCounts> PurgeAsync(
            EvidenceScope scope,
            DateTimeOffset? ingestedBefore,
            DateTimeOffset now,
            Func<EvidencePurgeCounts, AuditEventDraft?> audit,
            CancellationToken cancellationToken)
        {
            PurgeCalls++;
            PurgedWithoutCutoff = ingestedBefore is null;
            var counts = CountsFor(scope.Id);
            if (audit(counts) is { } draft)
            {
                Audits.Add(draft);
            }

            return Task.FromResult(counts);
        }
    }
}
