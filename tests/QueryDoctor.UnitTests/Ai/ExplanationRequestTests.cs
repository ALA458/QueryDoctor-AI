using QueryDoctor.Application.Explanations;
using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Ai;

namespace QueryDoctor.UnitTests.Ai;

/// <summary>
/// Lado de la Api (S2-05): pedir una explicación encola solo el id (invariante 2); sin hallazgos se indica y no se
/// encola (CA-HU09.2); con la IA caída se ven los hallazgos y el aviso (CA-HU12.1).
/// </summary>
public sealed class ExplanationRequestTests
{
    private readonly ExplainQueryHarness _harness = new();
    private readonly RecordingScheduler _scheduler = new();

    private RequestExplanation Request() => new(_harness.Store, _scheduler, TimeProvider.System);

    private GetLatestExplanation Latest() => new(_harness.Store, _harness.Encryptor, ExplainQueryHarness.Catalog, TimeProvider.System);

    [Fact]
    public async Task ConHallazgosSeGuardaPendienteYSeEncolaSoloElId()
    {
        var queryId = _harness.SeedQuery();

        var result = await Request().ExecuteAsync(queryId);

        var accepted = Assert.IsType<RequestExplanationResult.Accepted>(result);
        Assert.Equal([accepted.ExplanationId], _scheduler.Enqueued);
        var saved = Assert.Single(_harness.Store.Explanations);
        Assert.Equal(ExplanationState.Pending, saved.State);
        Assert.NotEqual(Guid.Empty, saved.CorrelationId);
    }

    [Fact]
    public async Task SinHallazgosNoSeGeneraYSeIndicaSinEncolar()
    {
        // CA-HU09.2.
        var queryId = _harness.SeedQueryWithoutFindings();

        var result = await Request().ExecuteAsync(queryId);

        Assert.IsType<RequestExplanationResult.NoFindings>(result);
        Assert.Empty(_scheduler.Enqueued);
        Assert.Empty(_harness.Store.Explanations);
    }

    [Fact]
    public async Task UnaConsultaInexistenteNoEncola()
    {
        Assert.IsType<RequestExplanationResult.QueryNotFound>(await Request().ExecuteAsync(Guid.NewGuid()));
        Assert.Empty(_scheduler.Enqueued);
    }

    [Fact]
    public async Task ConUnaPendienteRecienteSeRespondeEnCursoSinEncolarOtra()
    {
        // E10.
        var queryId = _harness.SeedQuery();
        var first = Assert.IsType<RequestExplanationResult.Accepted>(await Request().ExecuteAsync(queryId));

        var second = await Request().ExecuteAsync(queryId);

        Assert.Equal(first.ExplanationId, Assert.IsType<RequestExplanationResult.AlreadyInProgress>(second).ExplanationId);
        Assert.Single(_scheduler.Enqueued);
    }

    [Fact]
    public async Task UnaPendienteAbandonadaSeCierraYSeEncolaUnaNueva()
    {
        var queryId = _harness.SeedQuery();
        var stale = _harness.Request(queryId);
        stale.RequestedAt = DateTimeOffset.UtcNow - RequestExplanation.ActiveRequestWindow - TimeSpan.FromMinutes(1);

        var result = await Request().ExecuteAsync(queryId);

        Assert.IsType<RequestExplanationResult.Accepted>(result);
        Assert.Equal(ExplanationState.Unavailable, stale.State);
        Assert.Equal(ExplanationCause.Abandoned, stale.Cause);
    }

    [Fact]
    public async Task ConLaIaCaidaSeVenLosHallazgosYElAvisoDeIndisponibilidad()
    {
        // CA-HU12.1 / RF-IA-06.
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);
        await _harness.Create(ScriptedAiExplanationClient.Throwing(new Application.Ai.AiServiceUnavailableException(ExplanationCause.Timeout)))
            .ExecuteAsync(explanation.Id, CancellationToken.None);

        var view = Assert.IsType<GetLatestExplanationResult.Found>(await Latest().ExecuteAsync(queryId)).View;

        Assert.Equal(ExplanationCodes.StateUnavailable, view.Status);
        Assert.Equal(nameof(ExplanationCause.Timeout), view.Cause);
        Assert.Equal(ExplanationCodes.UnavailableNotice, view.Notice);
        Assert.NotEmpty(view.Findings);
        Assert.All(view.Findings, f => Assert.False(string.IsNullOrWhiteSpace(f.RuleName)));
        Assert.Empty(view.Claims);
    }

    [Fact]
    public async Task UnaTrazadaSeMuestraConSusAfirmacionesYElAvisoPersistente()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);
        await _harness.Create(new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse))
            .ExecuteAsync(explanation.Id, CancellationToken.None);

        var view = Assert.IsType<GetLatestExplanationResult.Found>(await Latest().ExecuteAsync(queryId)).View;

        Assert.Equal(ExplanationCodes.StateCompleted, view.Status);
        Assert.Equal(ExplanationValidationConstants.StatusTrazadaCode, view.ValidationStatus);
        Assert.Equal(ExplanationValidationConstants.PersistentNoticeText, view.Notice);
        Assert.NotEmpty(view.Claims);
        Assert.False(view.FromCache);
    }

    [Fact]
    public async Task UnaEnCursoAbandonadaSeMuestraComoNoDisponibleSinModificarla()
    {
        var queryId = _harness.SeedQuery();
        var stale = _harness.Request(queryId);
        stale.State = ExplanationState.Running;
        stale.RequestedAt = DateTimeOffset.UtcNow - RequestExplanation.ActiveRequestWindow - TimeSpan.FromMinutes(1);

        var view = Assert.IsType<GetLatestExplanationResult.Found>(await Latest().ExecuteAsync(queryId)).View;

        Assert.Equal(ExplanationCodes.StateUnavailable, view.Status);
        Assert.Equal(nameof(ExplanationCause.Abandoned), view.Cause);
        Assert.Equal(ExplanationState.Running, stale.State);
    }

    [Fact]
    public async Task SinSolicitudPreviaSeIndicaQueNoSePidio()
    {
        var queryId = _harness.SeedQuery();

        Assert.IsType<GetLatestExplanationResult.NotRequested>(await Latest().ExecuteAsync(queryId));
        Assert.IsType<GetLatestExplanationResult.QueryNotFound>(await Latest().ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public void NingunTextoFijoDiceVerificadaNiValidada()
    {
        // RF-IA-10 / invariante 6.
        foreach (var text in new[] { ExplanationCodes.UnavailableNotice, ExplanationCodes.NoFindingsNotice, ExplanationCodes.NoSendableFindingsNotice })
        {
            Assert.DoesNotContain("verificada", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("validada", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class RecordingScheduler : IExplanationJobScheduler
    {
        public List<Guid> Enqueued { get; } = [];

        public void Enqueue(Guid explanationId) => Enqueued.Add(explanationId);
    }
}
