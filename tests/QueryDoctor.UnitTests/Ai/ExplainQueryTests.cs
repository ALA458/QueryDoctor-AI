using System.Reflection;
using Hangfire;
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Infrastructure.Explanations;
using QueryDoctor.UnitTests.Masking;

namespace QueryDoctor.UnitTests.Ai;

/// <summary>
/// Trabajo explain (S2-05): generación con validación en .NET (RF-IA-03), caché por hash (RF-IA-07, CA-HU09.3),
/// degradación cuando la IA falla (RF-IA-06, CA-HU12.1) y sin hallazgos no se llama a la IA (RF-IA-01, CA-HU09.2).
/// </summary>
public sealed class ExplainQueryTests
{
    private readonly ExplainQueryHarness _harness = new();

    [Fact]
    public async Task ConRespuestaValidaQuedaTrazadaConCuerpoCifradoYDatosDeTrazabilidad()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);

        var outcome = await _harness.Create(client).ExecuteAsync(explanation.Id, CancellationToken.None);

        Assert.NotNull(outcome);
        Assert.Equal(ExplanationState.Completed, explanation.State);
        Assert.Equal(ExplanationValidationConstants.StatusTrazadaCode, explanation.ValidationStatus);
        Assert.Null(explanation.Cause);
        Assert.False(outcome.FromCache);
        Assert.Single(client.RecordedBodies);

        // RNF-SEG-14: el cuerpo se guarda cifrado; RF-TRZ-01: hash, catálogo, hallazgos, modelo y prompt.
        Assert.StartsWith("enc:", explanation.EncryptedBody, StringComparison.Ordinal);
        Assert.Equal(64, explanation.ContextHash!.Length);
        Assert.Equal(ExplainQueryHarness.Catalog.CatalogVersion, explanation.CatalogVersion);
        Assert.Contains("F-", explanation.FindingCodesJson, StringComparison.Ordinal);
        Assert.Equal("mock-model", explanation.Model);
        Assert.Equal("v1", explanation.PromptVersion);
        Assert.NotNull(explanation.StartedAt);
        Assert.NotNull(explanation.CompletedAt);

        var body = _harness.Body(explanation);
        Assert.Equal(ExplanationValidationConstants.StatusTrazadaCode, body.Status);
        Assert.Equal(ExplanationValidationConstants.PersistentNoticeText, body.PersistentNotice);
    }

    [Fact]
    public async Task CadaAfirmacionSeGuardaCifradaConSusHallazgosYLosNodosDelPlan()
    {
        // §7 (ai.claim, ai.claim_reference) y RF-DASH-05: enlaces a los hallazgos y a los nodos que sustentan.
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);

        await _harness.Create(client).ExecuteAsync(explanation.Id, CancellationToken.None);

        var claims = _harness.Store.Claims.Where(c => c.ExplanationId == explanation.Id).OrderBy(c => c.ClaimIndex).ToArray();
        Assert.NotEmpty(claims);
        Assert.Equal(Enumerable.Range(0, claims.Length), claims.Select(c => c.ClaimIndex));
        Assert.All(claims, claim =>
        {
            Assert.Equal("OBSERVACION", claim.ClaimType);
            Assert.True(claim.Passed);
            Assert.StartsWith("enc:", claim.EncryptedText, StringComparison.Ordinal);
            Assert.Equal("El motor señaló este operador del plan.", _harness.Encryptor.Decrypt(claim.EncryptedText));
            var reference = Assert.Single(claim.References);
            Assert.StartsWith("F-", reference.FindingCode, StringComparison.Ordinal);
            Assert.NotEqual("[]", reference.NodeIdsJson);
        });
    }

    [Fact]
    public async Task UnHallazgoCitadoQueNoEstaEnElContextoQuedaSinNodosYLaAfirmacionNoPasa()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);

        await _harness.Create(new ScriptedAiExplanationClient(ExplainQueryHarness.UntracedResponse))
            .ExecuteAsync(explanation.Id, CancellationToken.None);

        var claim = Assert.Single(_harness.Store.Claims);
        Assert.False(claim.Passed);
        var reference = Assert.Single(claim.References);
        Assert.Equal("F-NO-EXISTE", reference.FindingCode);
        Assert.Equal("[]", reference.NodeIdsJson);
    }

    [Fact]
    public async Task LoQueSaleHaciaLaIaNoLlevaNingunCentinela()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);

        await _harness.Create(client).ExecuteAsync(explanation.Id, CancellationToken.None);

        var body = Assert.Single(client.RecordedBodies);
        MaskingSentinels.AssertNoneIn(body);
        Assert.Contains("@qd_", body, StringComparison.Ordinal);
        Assert.Contains("\"kb_fragments\":[{", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnaRespuestaQueNoSuperaElValidadorQuedaNoVerificadaYNoIndisponible()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);

        await _harness.Create(new ScriptedAiExplanationClient(ExplainQueryHarness.UntracedResponse))
            .ExecuteAsync(explanation.Id, CancellationToken.None);

        Assert.Equal(ExplanationState.Completed, explanation.State);
        Assert.Equal(ExplanationValidationConstants.StatusNoVerificadaCode, explanation.ValidationStatus);
        Assert.Contains(_harness.Body(explanation).ValidationFailures, f => f.Code == nameof(ValidationFailureCode.UnknownFindingId));
    }

    [Theory]
    [InlineData(ExplanationCause.Timeout)]
    [InlineData(ExplanationCause.ServiceUnreachable)]
    [InlineData(ExplanationCause.ProviderError)]
    [InlineData(ExplanationCause.InvalidOutput)]
    public async Task SiLaIaFallaQuedaIndisponibleConCausaYLosHallazgosSiguenVisibles(ExplanationCause cause)
    {
        // CA-HU12.1 / RF-IA-06: sin cuerpo, con causa, y los hallazgos del análisis no se tocan.
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);

        var outcome = await _harness.Create(ScriptedAiExplanationClient.Throwing(new AiServiceUnavailableException(cause)))
            .ExecuteAsync(explanation.Id, CancellationToken.None);

        Assert.Equal(ExplanationState.Unavailable, explanation.State);
        Assert.Equal(cause, explanation.Cause);
        Assert.Null(explanation.EncryptedBody);
        Assert.Null(explanation.ValidationStatus);
        Assert.NotNull(explanation.CompletedAt);
        Assert.True(outcome!.FindingsSent > 0);
        Assert.NotEmpty(await _harness.Store.GetLatestFindingsAsync(queryId, CancellationToken.None));
    }

    [Fact]
    public async Task UnCuerpoQueNoEsJsonSeTrataComoSalidaNoValida()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);

        await _harness.Create(new ScriptedAiExplanationClient(_ => "<html>error</html>"))
            .ExecuteAsync(explanation.Id, CancellationToken.None);

        Assert.Equal(ExplanationState.Unavailable, explanation.State);
        Assert.Equal(ExplanationCause.InvalidOutput, explanation.Cause);
    }

    [Fact]
    public async Task ConElMismoContextoSeReutilizaLaTrazadaSinLlamarDeNuevoALaIa()
    {
        // CA-HU09.3 / RF-IA-07 / CU-05 1a.
        var queryId = _harness.SeedQuery();
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);
        var explain = _harness.Create(client);

        var first = _harness.Request(queryId);
        await explain.ExecuteAsync(first.Id, CancellationToken.None);
        var second = _harness.Request(queryId);
        var outcome = await explain.ExecuteAsync(second.Id, CancellationToken.None);

        Assert.Single(client.RecordedBodies);
        Assert.True(outcome!.FromCache);
        Assert.Equal(ExplanationState.Completed, second.State);
        Assert.Equal(first.Id, second.ReusedFromId);
        Assert.Equal(first.ContextHash, second.ContextHash);
        Assert.Equal(first.EncryptedBody, second.EncryptedBody);
        Assert.Equal(ExplanationValidationConstants.StatusTrazadaCode, second.ValidationStatus);
    }

    [Fact]
    public async Task ConOtroPromptNoSeReutilizaLaExplicacion()
    {
        // RF-IA-07: el prompt y el perfil de LLM forman parte del contexto.
        var queryId = _harness.SeedQuery();
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);
        var explain = _harness.Create(client);
        await explain.ExecuteAsync(_harness.Request(queryId).Id, CancellationToken.None);

        client.Profile = ExplainQueryHarness.Profile with { PromptVersion = "v2" };
        var outcome = await explain.ExecuteAsync(_harness.Request(queryId).Id, CancellationToken.None);

        Assert.Equal(2, client.RecordedBodies.Count);
        Assert.False(outcome!.FromCache);
    }

    [Fact]
    public async Task SiNoSePuedeLeerElPerfilNoSeUsaLaCacheYSeGeneraIgual()
    {
        var queryId = _harness.SeedQuery();
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);
        var explain = _harness.Create(client);
        await explain.ExecuteAsync(_harness.Request(queryId).Id, CancellationToken.None);

        client.Profile = null;
        var second = _harness.Request(queryId);
        var outcome = await explain.ExecuteAsync(second.Id, CancellationToken.None);

        Assert.Equal(2, client.RecordedBodies.Count);
        Assert.False(outcome!.FromCache);
        Assert.Equal(ExplanationState.Completed, second.State);
    }

    [Fact]
    public async Task UnaNoVerificadaNoSeReutiliza()
    {
        var queryId = _harness.SeedQuery();
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.UntracedResponse);
        var explain = _harness.Create(client);

        await explain.ExecuteAsync(_harness.Request(queryId).Id, CancellationToken.None);
        var outcome = await explain.ExecuteAsync(_harness.Request(queryId).Id, CancellationToken.None);

        Assert.Equal(2, client.RecordedBodies.Count);
        Assert.False(outcome!.FromCache);
    }

    [Fact]
    public async Task SinHallazgosNoSeLlamaALaIa()
    {
        var queryId = _harness.SeedQueryWithoutFindings();
        var explanation = _harness.Request(queryId);
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);

        await _harness.Create(client).ExecuteAsync(explanation.Id, CancellationToken.None);

        Assert.Equal(ExplanationState.NotGenerated, explanation.State);
        Assert.Equal(ExplanationCause.NoFindings, explanation.Cause);
        Assert.Empty(client.RecordedBodies);
    }

    [Fact]
    public async Task SiNingunHallazgoEsEnviableNoSeLlamaALaIa()
    {
        // Fallo cerrado de S2-04: una clave de evidencia sin clasificar deja fuera el hallazgo.
        var queryId = _harness.SeedQuery(alter: f => f with
        {
            Evidence = new Dictionary<string, string?> { ["clave_sin_clasificar"] = MaskingSentinels.Secret },
        });
        var explanation = _harness.Request(queryId);
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);

        await _harness.Create(client).ExecuteAsync(explanation.Id, CancellationToken.None);

        Assert.Equal(ExplanationState.NotGenerated, explanation.State);
        Assert.Equal(ExplanationCause.NoSendableFindings, explanation.Cause);
        Assert.Empty(client.RecordedBodies);
    }

    [Fact]
    public async Task UnTrabajoRepetidoNoVuelveALlamarALaIa()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);
        var client = new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse);
        var explain = _harness.Create(client);

        await explain.ExecuteAsync(explanation.Id, CancellationToken.None);
        await explain.ExecuteAsync(explanation.Id, CancellationToken.None);

        Assert.Single(client.RecordedBodies);
    }

    [Fact]
    public async Task UnFalloInesperadoDejaConstanciaYSeRelanza()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _harness.Create(ScriptedAiExplanationClient.Throwing(new InvalidOperationException("boom")))
                .ExecuteAsync(explanation.Id, CancellationToken.None));

        Assert.Equal(ExplanationState.Unavailable, explanation.State);
        Assert.Equal(ExplanationCause.Unexpected, explanation.Cause);
    }

    [Fact]
    public async Task UnaExplicacionInexistenteDevuelveNull()
    {
        var outcome = await _harness.Create(new ScriptedAiExplanationClient(ExplainQueryHarness.TracedResponse))
            .ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(outcome);
    }

    [Fact]
    public void ElTrabajoVaEnLaColaExplainSinReintentosDeHangfire()
    {
        // E1: el servicio de IA ya reintenta hacia el LLM; reintentar aquí multiplicaría las llamadas.
        var method = typeof(ExplainQueryJob).GetMethod(nameof(ExplainQueryJob.RunAsync))!;

        Assert.Equal(JobQueues.Explain, method.GetCustomAttribute<QueueAttribute>()!.Queue);
        Assert.Equal(0, method.GetCustomAttribute<AutomaticRetryAttribute>()!.Attempts);
        Assert.Equal([typeof(Guid), typeof(CancellationToken)], method.GetParameters().Select(p => p.ParameterType));
    }

}
