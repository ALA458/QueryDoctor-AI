using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Persistence.Seeds;

namespace QueryDoctor.UnitTests.Validation;

public sealed class ExplanationValidatorTests
{
    private readonly ExplanationValidator _validator = new();

    [Fact]
    public void ValidateJsonValidExplanationWithCuratedFragmentsReturnsTrazadaWithPersistentNotice()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG01-1",
                    "REG-01",
                    EvidenceClass.Estimada,
                    [4],
                    new Dictionary<string, string>
                    {
                        ["estimated_rows"] = "125000",
                        ["predicate"] = "CONVERT_IMPLICIT(nvarchar(50), [c].[Code], @qd_num_1)=@qd_str_1",
                    }),
                new ValidatedFindingInput(
                    "F-REG02-1",
                    "REG-02",
                    EvidenceClass.PlanReal,
                    [7],
                    new Dictionary<string, string>
                    {
                        ["impact_pct"] = "84.567",
                    }),
            ],
            metrics: new Dictionary<string, double>
            {
                ["avg_cpu_ms"] = 312.4,
            });

        var rawJson = SerializeResponse(
            context.QueryId,
            "La consulta presenta cuello de botella de 84.6% de impacto y CPU promedio de 312.4 ms.",
            [
                new AiClaimPayload(
                    "OBSERVACION",
                    "En el nodo 4 (REG-01) se estiman 125,000 filas afectadas con parámetro @qd_str_1 y columna nvarchar(50).",
                    ["F-REG01-1"],
                    null),
                new AiClaimPayload(
                    "INTERPRETACION",
                    "La conversión implícita degrada el plan en REG-01 y obliga a recorrer 125 000 filas.",
                    ["F-REG01-1"],
                    KnowledgeBaseSeedData.FragmentReg01InterpretacionId.ToString()),
                new AiClaimPayload(
                    "RECOMENDACION",
                    "Evaluar un índice no agrupado para el hallazgo F-REG02-1 con impacto estimado de 84.6%.",
                    ["F-REG02-1"],
                    KnowledgeBaseSeedData.FragmentReg02RecomendacionId.ToString()),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.Trazada, result.Status);
        Assert.Equal("TRAZADA", result.StatusCode);
        Assert.True(result.IsTraced);
        Assert.Equal(ExplanationValidationConstants.PersistentNoticeText, result.PersistentNotice);
        Assert.Empty(result.Failures);

        Assert.Equal(3, result.Claims.Count);
        Assert.All(result.Claims, static c => Assert.True(c.Passed));

        Assert.False(result.Claims[0].IsIndicio);
        Assert.Null(result.Claims[0].IndicationLabel);

        Assert.True(result.Claims[1].IsIndicio);
        Assert.Equal("indicio", result.Claims[1].IndicationLabel);

        Assert.False(result.Claims[2].IsIndicio);
        Assert.Null(result.Claims[2].IndicationLabel);

        AssertPositiveLabelsNeverContainProhibitedWords(result);
    }

    [Fact]
    public void ValidateJsonAlteredNumberInClaimReturnsNoVerificadaAndNumericMismatch()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG03-1",
                    "REG-03",
                    EvidenceClass.Estimada,
                    [2],
                    new Dictionary<string, string>
                    {
                        ["estimated_rows_read"] = "250000",
                        ["rows_read_to_returned_ratio"] = "50.0",
                    }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "El escaneo completo lee 250,000 filas.",
            [
                new AiClaimPayload(
                    "OBSERVACION",
                    "El operador lee 999,000 filas con una proporción de 50.0x.",
                    ["F-REG03-1"],
                    null),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.NoVerificada, result.Status);
        Assert.Equal("NO_VERIFICADA", result.StatusCode);
        Assert.False(result.IsTraced);
        Assert.Null(result.PersistentNotice);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(ValidationFailureCode.NumericMismatch, failure.Code);
        Assert.Equal(0, failure.ClaimIndex);
        Assert.Contains("999,000", failure.Detail, StringComparison.Ordinal);
        Assert.False(result.Claims[0].Passed);
    }

    [Fact]
    public void ValidateJsonAlteredNumberInSummaryReturnsNoVerificada()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG01-1",
                    "REG-01",
                    EvidenceClass.Estimada,
                    [1],
                    new Dictionary<string, string> { ["estimated_rows"] = "10000" }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "Resumen: la consulta procesa 75,000 filas de forma ineficiente.",
            [
                new AiClaimPayload(
                    "OBSERVACION",
                    "Se estiman 10,000 filas en el operador afectado.",
                    ["F-REG01-1"],
                    null),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.NoVerificada, result.Status);
        Assert.Contains(result.Failures, static f => f.Code == ValidationFailureCode.NumericMismatch && f.ClaimIndex is null);
    }

    [Fact]
    public void ValidateJsonNumberFromUncitedFindingDoesNotSupportOtherClaim()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-A",
                    "REG-01",
                    EvidenceClass.Estimada,
                    [1],
                    new Dictionary<string, string> { ["estimated_rows"] = "12000" }),
                new ValidatedFindingInput(
                    "F-B",
                    "REG-04",
                    EvidenceClass.Estimada,
                    [5],
                    new Dictionary<string, string> { ["estimated_executions"] = "9500" }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "Análisis combinado.",
            [
                new AiClaimPayload(
                    "OBSERVACION",
                    "El hallazgo reporta 9,500 filas estimadas.",
                    ["F-A"],
                    null),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.NoVerificada, result.Status);
        Assert.Contains(result.Failures, static f => f.Code == ValidationFailureCode.NumericMismatch && f.ClaimIndex == 0);
    }

    [Fact]
    public void ValidateJsonInterpretacionWithoutCuratedFragmentReturnsNoVerificada()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG01-1",
                    "REG-01",
                    EvidenceClass.Estimada,
                    [1],
                    new Dictionary<string, string> { ["estimated_rows"] = "15000" }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "Interpretación sin respaldo curado.",
            [
                new AiClaimPayload(
                    "INTERPRETACION",
                    "La conversión implícita degrada el rendimiento sobre 15,000 filas.",
                    ["F-REG01-1"],
                    null),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.NoVerificada, result.Status);
        Assert.Null(result.PersistentNotice);
        Assert.Contains(result.Failures, static f => f.Code == ValidationFailureCode.MissingKbFragmentForCausalClaim);
    }

    [Fact]
    public void ValidateJsonNonGuidFragmentCodeLikeMockOrPromptExampleReturnsNoVerificada()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG01-1",
                    "REG-01",
                    EvidenceClass.Estimada,
                    [1],
                    new Dictionary<string, string> { ["estimated_rows"] = "15000" }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "Explicación con formato antiguo no Guid.",
            [
                new AiClaimPayload(
                    "INTERPRETACION",
                    "La conversión implícita afecta 15,000 filas.",
                    ["F-REG01-1"],
                    "KB-REG01-01"),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.NoVerificada, result.Status);
        Assert.Contains(result.Failures, static f => f.Code == ValidationFailureCode.InvalidKbFragmentIdFormat);
    }

    [Fact]
    public void ValidateJsonFragmentWithoutCausalRelationForRuleOrSwappedRelationTypeReturnsNoVerificada()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG01-1",
                    "REG-01",
                    EvidenceClass.Estimada,
                    [1],
                    new Dictionary<string, string> { ["estimated_rows"] = "20000" }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "Explicación con relaciones inválidas.",
            [
                new AiClaimPayload(
                    "INTERPRETACION",
                    "Causa ajena sobre 20,000 filas.",
                    ["F-REG01-1"],
                    KnowledgeBaseSeedData.FragmentReg02InterpretacionId.ToString()),
                new AiClaimPayload(
                    "INTERPRETACION",
                    "Tipo de relación invertido sobre 20,000 filas.",
                    ["F-REG01-1"],
                    KnowledgeBaseSeedData.FragmentReg01RecomendacionId.ToString()),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.NoVerificada, result.Status);
        Assert.Equal(2, result.Failures.Count(static f => f.Code == ValidationFailureCode.UnverifiedCausalRelation));
    }

    [Fact]
    public void ValidateJsonMultipleRulesCitedInCausalClaimRequiresAllRulesToHaveCuratedRelation()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG01-1",
                    "REG-01",
                    EvidenceClass.Estimada,
                    [1],
                    new Dictionary<string, string> { ["estimated_rows"] = "20000" }),
                new ValidatedFindingInput(
                    "F-REG03-1",
                    "REG-03",
                    EvidenceClass.Estimada,
                    [2],
                    new Dictionary<string, string> { ["estimated_rows_read"] = "50000" }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "Afirmación multi-regla.",
            [
                new AiClaimPayload(
                    "INTERPRETACION",
                    "Impacto conjunto sobre 20,000 y 50,000 filas.",
                    ["F-REG01-1", "F-REG03-1"],
                    KnowledgeBaseSeedData.FragmentReg01InterpretacionId.ToString()),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.NoVerificada, result.Status);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(ValidationFailureCode.UnverifiedCausalRelation, failure.Code);
        Assert.Contains("REG-03", failure.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateJsonUnknownFindingIdOrEmptyFindingIdsReturnsNoVerificada()
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG04-1",
                    "REG-04",
                    EvidenceClass.Estimada,
                    [5],
                    new Dictionary<string, string> { ["estimated_executions"] = "500" }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "Falta de trazabilidad de IDs.",
            [
                new AiClaimPayload("OBSERVACION", "Se observan 500 ejecuciones.", [], null),
                new AiClaimPayload("OBSERVACION", "Se observan 500 ejecuciones.", ["F-INEXISTENTE"], null),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.NoVerificada, result.Status);
        Assert.Contains(result.Failures, static f => f.Code == ValidationFailureCode.MissingFindingReferences && f.ClaimIndex == 0);
        Assert.Contains(result.Failures, static f => f.Code == ValidationFailureCode.UnknownFindingId && f.ClaimIndex == 1);
    }

    [Theory]
    [InlineData(EvidenceClass.Estimada, true, "indicio")]
    [InlineData(EvidenceClass.EjecucionAgregada, true, "indicio")]
    [InlineData(EvidenceClass.PlanReal, false, null)]
    public void ValidateJsonInterpretacionAssignsDeterministicIndicioLabelByEvidenceClass(
        EvidenceClass evidenceClass,
        bool expectedIndicio,
        string? expectedLabel)
    {
        var context = CreateSeededContext(
            findings:
            [
                new ValidatedFindingInput(
                    "F-REG01-1",
                    "REG-01",
                    evidenceClass,
                    [1],
                    new Dictionary<string, string> { ["estimated_rows"] = "10000" }),
            ]);

        var rawJson = SerializeResponse(
            context.QueryId,
            "Explicación determinista.",
            [
                new AiClaimPayload(
                    "INTERPRETACION",
                    "Esto demuestra de forma definitiva que las 10,000 filas causan el problema.",
                    ["F-REG01-1"],
                    KnowledgeBaseSeedData.FragmentReg01InterpretacionId.ToString()),
            ]);

        var result = _validator.ValidateJson(rawJson, context);

        Assert.Equal(ExplanationValidationStatus.Trazada, result.Status);
        var claim = Assert.Single(result.Claims);
        Assert.Equal(expectedIndicio, claim.IsIndicio);
        Assert.Equal(expectedLabel, claim.IndicationLabel);
    }

    [Fact]
    public void ValidateJsonMalformedJsonOrQueryIdMismatchReturnsNoVerificada()
    {
        var context = CreateSeededContext([]);

        var malformed = _validator.ValidateJson("{not valid json", context);
        Assert.Equal(ExplanationValidationStatus.NoVerificada, malformed.Status);
        Assert.Null(malformed.PersistentNotice);
        Assert.Contains(malformed.Failures, static f => f.Code == ValidationFailureCode.InvalidJson);

        var mismatchedQuery = SerializeResponse(
            Guid.NewGuid().ToString(),
            "Resumen.",
            [new AiClaimPayload("OBSERVACION", "Sin cifras.", ["F-1"], null)]);

        var mismatchResult = _validator.ValidateJson(mismatchedQuery, context);
        Assert.Equal(ExplanationValidationStatus.NoVerificada, mismatchResult.Status);
        Assert.Contains(mismatchResult.Failures, static f => f.Code == ValidationFailureCode.QueryIdMismatch);
    }

    [Fact]
    public async Task ContextBuilderBuildsFromMaskedFindingsAndRepositoryAndValidatesEndToEnd()
    {
        var masker = new LiteralMasker();
        var finding = new FindingContract(
            FindingContract.CurrentSchemaVersion,
            "F-MASKED-01",
            "1.0.0",
            new FindingRuleContract("REG-04", "Excessive Key Lookup", "1.0.0"),
            "SENAL",
            "ALTA",
            "ESTIMADA",
            QueryId: 42,
            PlanId: 100,
            Nodes: [new FindingNodeContract(8, "Key Lookup")],
            Evidence: new Dictionary<string, string?>
            {
                ["estimated_executions"] = "2500",
                ["table"] = "[dbo].[Orders]",
            },
            Context: new FindingContextContract(Executions: 2500, EstimatedRows: 2500),
            ThresholdsUsed: new Dictionary<string, double> { ["Reg04MinEstimatedExecutions"] = 100 },
            EngineVersion: "16.0.4150.1",
            AnalyzedAt: new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));

        var maskedContext = masker.Mask(new MaskingInput(
            "SELECT Id FROM dbo.Orders WHERE Status = 'OPEN'",
            Findings: [finding]));

        Assert.Single(maskedContext.Findings);

        var builder = new ExplanationValidationContextBuilder(new SeededInMemoryKnowledgeBaseRepository());
        var queryId = "8c902a10-7711-4433-9922-000000000042";

        var validationContext = await builder.BuildAsync(
            queryId,
            maskedContext.Findings,
            new Dictionary<string, double> { ["avg_logical_reads"] = 18400 },
            engineVersion: "16.0.4150.1");

        var rawJson = SerializeResponse(
            queryId,
            "La consulta promedia 18,400 lecturas lógicas bajo la versión 16.0.4150.1.",
            [
                new AiClaimPayload(
                    "OBSERVACION",
                    "El operador en el nodo 8 ejecuta 2,500 búsquedas sobre [dbo].[Orders] (REG-04).",
                    ["F-MASKED-01"],
                    null),
                new AiClaimPayload(
                    "INTERPRETACION",
                    "Cada una de las 2,500 búsquedas aleatorias genera saltos de página adicionales.",
                    ["F-MASKED-01"],
                    KnowledgeBaseSeedData.FragmentReg04InterpretacionId.ToString()),
                new AiClaimPayload(
                    "RECOMENDACION",
                    "Cubrir las columnas proyectadas evita repetir las 2,500 operaciones Key Lookup.",
                    ["F-MASKED-01"],
                    KnowledgeBaseSeedData.FragmentReg04RecomendacionId.ToString()),
            ]);

        var result = _validator.ValidateJson(rawJson, validationContext);

        Assert.Empty(result.Failures);
        Assert.Equal(ExplanationValidationStatus.Trazada, result.Status);
        Assert.True(result.Claims[1].IsIndicio);
        Assert.False(result.Claims[2].IsIndicio);
        AssertPositiveLabelsNeverContainProhibitedWords(result);
    }

    [Fact]
    public void AddInfrastructureBindsExplanationValidationOptionsAndRegistersValidator()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExplanationValidation:RelativeTolerance"] = "0.01",
                ["ExplanationValidation:ValidateSummaryNumbers"] = "false",
                ["ExplanationValidation:RequireAllCitedRulesForCausalRelation"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(config);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<ExplanationValidationOptions>();
        var validator = provider.GetRequiredService<IExplanationValidator>();

        Assert.Equal(0.01, options.RelativeTolerance, 6);
        Assert.False(options.ValidateSummaryNumbers);
        Assert.True(options.RequireAllCitedRulesForCausalRelation);
        Assert.IsType<ExplanationValidator>(validator);
    }

    private static ExplanationValidationContext CreateSeededContext(
        IReadOnlyList<ValidatedFindingInput> findings,
        IReadOnlyDictionary<string, double>? metrics = null)
    {
        var fragments = KnowledgeBaseSeedData.GetFragments()
            .Select(static f => f.Id)
            .ToHashSet();

        var relations = KnowledgeBaseSeedData.GetCausalRelations()
            .Select(static r => new CuratedCausalRelation(r.RuleId, r.FragmentId, r.RelationType))
            .ToHashSet();

        return new ExplanationValidationContext(
            "11111111-1111-1111-1111-111111111111",
            findings,
            metrics ?? new Dictionary<string, double>(),
            fragments,
            relations,
            "16.0.4150.1");
    }

    private static string SerializeResponse(
        string queryId,
        string summary,
        IReadOnlyList<AiClaimPayload> claims)
    {
        var payload = new AiExplanationPayload(
            queryId,
            summary,
            claims,
            new AiExplanationMetadata("ollama_local", "gemma2:9b", "v1", "local", "private", 0.42));

        return JsonSerializer.Serialize(payload);
    }

    private static void AssertPositiveLabelsNeverContainProhibitedWords(ValidatedExplanationResult result)
    {
        Assert.DoesNotContain("verificada", result.StatusCode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validada", result.StatusCode, StringComparison.OrdinalIgnoreCase);

        if (result.PersistentNotice is not null)
        {
            Assert.DoesNotContain("verificada", result.PersistentNotice, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("validada", result.PersistentNotice, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var claim in result.Claims)
        {
            if (claim.IndicationLabel is not null)
            {
                Assert.DoesNotContain("verificada", claim.IndicationLabel, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("validada", claim.IndicationLabel, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private sealed class SeededInMemoryKnowledgeBaseRepository : Application.KnowledgeBase.IKnowledgeBaseRepository
    {
        private readonly IReadOnlyList<KbFragment> _fragments = KnowledgeBaseSeedData.GetFragments();
        private readonly IReadOnlyList<KbCausalRelation> _relations = KnowledgeBaseSeedData.GetCausalRelations();

        public Task<IReadOnlyList<KbFragment>> GetFragmentsByRuleAsync(string ruleId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KbFragment>>(
                _fragments.Where(f => string.Equals(f.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<KbFragment?> GetFragmentByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_fragments.FirstOrDefault(f => f.Id == id));

        public Task<IReadOnlyList<KbCausalRelation>> GetCausalRelationsAsync(string ruleId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KbCausalRelation>>(
                _relations.Where(r => string.Equals(r.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<bool> HasCausalRelationAsync(
            string ruleId,
            Guid fragmentId,
            CausalRelationType relationType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_relations.Any(r =>
                string.Equals(r.RuleId, ruleId, StringComparison.OrdinalIgnoreCase) &&
                r.FragmentId == fragmentId &&
                r.RelationType == relationType));
    }
}
