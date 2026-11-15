using QueryDoctor.Application.Explanations;
using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.UnitTests.Ai;

namespace QueryDoctor.UnitTests.Explanations;

/// <summary>
/// Pruebas unitarias de mapeo en <see cref="GetLatestExplanation"/> para avisos normativos en recomendaciones,
/// estado NO_VERIFICADA y etiquetas de indicio (S2-11 / RF-DASH-06, RF-IA-10, RF-IA-12).
/// </summary>
public sealed class GetLatestExplanationTests
{
    private readonly ExplainQueryHarness _harness = new();

    private GetLatestExplanation Latest() =>
        new(_harness.Store, _harness.Encryptor, ExplainQueryHarness.Catalog, TimeProvider.System);

    [Fact]
    public async Task RecomendacionDeIndiceFaltanteActivaRevisionHumanaYCostoDeEscritura()
    {
        var reassignedToReg02 = false;
        var queryId = _harness.SeedQuery(alter: c =>
        {
            if (reassignedToReg02)
            {
                return c;
            }

            reassignedToReg02 = true;
            return c with { Rule = c.Rule with { Id = "REG-02" } };
        });
        var findings = await _harness.Store.GetLatestFindingsAsync(queryId, CancellationToken.None);
        var reg02 = Assert.Single(findings, f => f.RuleId == "REG-02");
        var other = findings.First(f => f.RuleId != "REG-02");

        var body = new ExplanationBody(
            ExplanationValidationConstants.StatusTrazadaCode,
            ExplanationValidationConstants.PersistentNoticeText,
            "Resumen",
            []);
        var explanation = _harness.Request(queryId);
        explanation.State = ExplanationState.Completed;
        explanation.ValidationStatus = ExplanationValidationConstants.StatusTrazadaCode;
        explanation.EncryptedBody = _harness.Encryptor.Encrypt(body.ToJson());
        explanation.CompletedAt = DateTimeOffset.UtcNow;

        var c0Id = Guid.NewGuid();
        var c1Id = Guid.NewGuid();
        var c2Id = Guid.NewGuid();
        _harness.Store.Claims.AddRange(
        [
            new ExplanationClaim
            {
                Id = c0Id,
                ExplanationId = explanation.Id,
                ClaimIndex = 0,
                ClaimType = ExplanationValidationConstants.ClaimObservacionCode,
                EncryptedText = _harness.Encryptor.Encrypt("Observación sobre REG-02."),
                Passed = true,
                References = [new ExplanationClaimReference { Id = Guid.NewGuid(), ClaimId = c0Id, FindingCode = reg02.FindingCode, NodeIdsJson = "[1]" }],
            },
            new ExplanationClaim
            {
                Id = c1Id,
                ExplanationId = explanation.Id,
                ClaimIndex = 1,
                ClaimType = ExplanationValidationConstants.ClaimRecomendacionCode,
                EncryptedText = _harness.Encryptor.Encrypt("Evaluar índice sugerido."),
                Passed = true,
                References = [new ExplanationClaimReference { Id = Guid.NewGuid(), ClaimId = c1Id, FindingCode = reg02.FindingCode, NodeIdsJson = "[1]" }],
            },
            new ExplanationClaim
            {
                Id = c2Id,
                ExplanationId = explanation.Id,
                ClaimIndex = 2,
                ClaimType = ExplanationValidationConstants.ClaimRecomendacionCode,
                EncryptedText = _harness.Encryptor.Encrypt("Revisar predicado del operador."),
                Passed = true,
                References = [new ExplanationClaimReference { Id = Guid.NewGuid(), ClaimId = c2Id, FindingCode = other.FindingCode, NodeIdsJson = "[2]" }],
            },
        ]);

        var view = Assert.IsType<GetLatestExplanationResult.Found>(await Latest().ExecuteAsync(queryId)).View;

        Assert.Equal(3, view.Claims.Count);
        Assert.False(view.Claims[0].RequiresHumanReview);
        Assert.False(view.Claims[0].WriteCostWarning);

        Assert.True(view.Claims[1].RequiresHumanReview);
        Assert.True(view.Claims[1].WriteCostWarning);

        Assert.True(view.Claims[2].RequiresHumanReview);
        Assert.False(view.Claims[2].WriteCostWarning);
    }

    [Fact]
    public async Task ExplicacionNoVerificadaDevuelveFallosSinAvisoDeTrazada()
    {
        var queryId = _harness.SeedQuery();
        var explanation = _harness.Request(queryId);
        await _harness.Create(new ScriptedAiExplanationClient(ExplainQueryHarness.UntracedResponse))
            .ExecuteAsync(explanation.Id, CancellationToken.None);

        var view = Assert.IsType<GetLatestExplanationResult.Found>(await Latest().ExecuteAsync(queryId)).View;

        Assert.Equal(ExplanationCodes.StateCompleted, view.Status);
        Assert.Equal(ExplanationValidationConstants.StatusNoVerificadaCode, view.ValidationStatus);
        Assert.Null(view.Notice);
        Assert.NotEmpty(view.ValidationFailures);
        Assert.Contains(view.Claims, c => !c.Passed);
    }

    [Fact]
    public async Task InterpretacionConEtiquetaIndicioSeExponeEnLaVista()
    {
        var queryId = _harness.SeedQuery();
        var findings = await _harness.Store.GetLatestFindingsAsync(queryId, CancellationToken.None);
        var finding = findings[0];
        var body = new ExplanationBody(
            ExplanationValidationConstants.StatusTrazadaCode,
            ExplanationValidationConstants.PersistentNoticeText,
            "Resumen",
            []);
        var explanation = _harness.Request(queryId);
        explanation.State = ExplanationState.Completed;
        explanation.ValidationStatus = ExplanationValidationConstants.StatusTrazadaCode;
        explanation.EncryptedBody = _harness.Encryptor.Encrypt(body.ToJson());
        explanation.CompletedAt = DateTimeOffset.UtcNow;

        var claimId = Guid.NewGuid();
        _harness.Store.Claims.Add(new ExplanationClaim
        {
            Id = claimId,
            ExplanationId = explanation.Id,
            ClaimIndex = 0,
            ClaimType = ExplanationValidationConstants.ClaimInterpretacionCode,
            EncryptedText = _harness.Encryptor.Encrypt("Interpretación apoyada en filas estimadas."),
            IndicationLabel = ExplanationValidationConstants.IndicationLabel,
            Passed = true,
            References = [new ExplanationClaimReference { Id = Guid.NewGuid(), ClaimId = claimId, FindingCode = finding.FindingCode, NodeIdsJson = "[2]" }],
        });

        var view = Assert.IsType<GetLatestExplanationResult.Found>(await Latest().ExecuteAsync(queryId)).View;

        var claim = Assert.Single(view.Claims);
        Assert.Equal(ExplanationValidationConstants.IndicationLabel, claim.IndicationLabel);
        Assert.Equal([2], Assert.Single(claim.References).NodeIds);
    }
}
