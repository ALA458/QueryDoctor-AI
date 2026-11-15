using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Explanations;
using QueryDoctor.Domain.Ai;
using QueryDoctor.UnitTests.Ai;

namespace QueryDoctor.UnitTests.Traceability;

/// <summary>
/// S2-06, T3: toda explicación que llega a un estado final deja exactamente una entrada de traza (RF-TRZ-01,
/// CA-HU14.1); quedar en cola o en curso no deja ninguna.
/// </summary>
public sealed class ExplanationTraceTests
{
    private readonly ExplainQueryHarness _harness = new();

    [Fact]
    public async Task UnaExplicacionTrazadaDejaUnaEntradaConLoQuePideRfTrz01()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);

        await _harness.Create(new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse)).ExecuteAsync(explanation.Id, CancellationToken.None);

        var trace = Assert.Single(_harness.Store.Traces);
        Assert.Equal(explanation.Id, trace.ExplanationId);
        Assert.Equal(queryId, trace.QueryId);
        Assert.Equal(ExplanationCodes.StateCompleted, trace.State);
        Assert.Equal("TRAZADA", trace.ValidationStatus);
        Assert.Contains("F-", trace.FindingCodesJson, StringComparison.Ordinal);
        Assert.Equal(ExplainQueryHarness.Catalog.CatalogVersion, trace.CatalogVersion);
        Assert.Equal("mock-model", trace.Model);
        Assert.Equal("v1", trace.PromptVersion);
        Assert.Equal(64, trace.ContextHash!.Length);
    }

    [Fact]
    public async Task UnaExplicacionDeCacheTambienDejaSuEntradaConLaOriginal()
    {
        var queryId = _harness.SeedQuery();
        var explain = _harness.Create(new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse));
        var first = _harness.Request(queryId);
        await explain.ExecuteAsync(first.Id, CancellationToken.None);

        await explain.ExecuteAsync(_harness.Request(queryId).Id, CancellationToken.None);

        Assert.Equal(2, _harness.Store.Traces.Count);
        Assert.Equal(first.Id, _harness.Store.Traces[1].ReusedFromId);
    }

    [Fact]
    public async Task ConLaIaCaidaQuedaLaEntradaConLaCausa()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);

        await _harness.Create(ScriptedAiExplanationClient.Throwing(new AiServiceUnavailableException(ExplanationCause.Timeout)))
            .ExecuteAsync(explanation.Id, CancellationToken.None);

        var trace = Assert.Single(_harness.Store.Traces);
        Assert.Equal(ExplanationCodes.StateUnavailable, trace.State);
        Assert.Equal(nameof(ExplanationCause.Timeout), trace.Cause);
        Assert.Null(trace.ValidationStatus);
    }

    [Fact]
    public async Task SinHallazgosQuedaLaEntradaDeNoGenerada()
    {
        var queryId = _harness.SeedQueryWithoutFindings();

        await _harness.Create(new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse))
            .ExecuteAsync(_harness.Request(queryId).Id, CancellationToken.None);

        var trace = Assert.Single(_harness.Store.Traces);
        Assert.Equal(ExplanationCodes.StateNotGenerated, trace.State);
        Assert.Equal(nameof(ExplanationCause.NoFindings), trace.Cause);
    }

    [Fact]
    public async Task UnFalloInesperadoDejaUnaSolaEntrada()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _harness.Create(ScriptedAiExplanationClient.Throwing(new InvalidOperationException("boom")))
                .ExecuteAsync(explanation.Id, CancellationToken.None));

        Assert.Equal(nameof(ExplanationCause.Unexpected), Assert.Single(_harness.Store.Traces).Cause);
    }

    [Fact]
    public async Task UnaSolicitudAbandonadaTambienQuedaEnLaTraza()
    {
        var queryId = _harness.SeedQuery();
        var stale = _harness.Request(queryId);
        stale.RequestedAt = DateTimeOffset.UtcNow - RequestExplanation.ActiveRequestWindow - TimeSpan.FromMinutes(1);

        await new RequestExplanation(_harness.Store, new NoopScheduler(), TimeProvider.System).ExecuteAsync(queryId);

        var trace = Assert.Single(_harness.Store.Traces);
        Assert.Equal(stale.Id, trace.ExplanationId);
        Assert.Equal(nameof(ExplanationCause.Abandoned), trace.Cause);
    }

    [Fact]
    public async Task PedirUnaExplicacionNoDejaEntradaHastaQueTermina()
    {
        var queryId = _harness.SeedQuery();

        await new RequestExplanation(_harness.Store, new NoopScheduler(), TimeProvider.System).ExecuteAsync(queryId);

        Assert.Empty(_harness.Store.Traces);
    }

    private sealed class NoopScheduler : IExplanationJobScheduler
    {
        public void Enqueue(Guid explanationId)
        {
        }
    }
}
