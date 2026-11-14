using System.Globalization;
using System.Text.Json;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Validation;

/// <summary>
/// Implementación pura y determinista del validador de explicaciones de IA (S2-03, RF-IA-03, RF-IA-10, RF-IA-12, RNF-FUN-02).
/// Evalúa existencia de identificadores (a), concordancia numérica (b) y relación causa-efecto curada (c).
/// </summary>
public sealed class ExplanationValidator : IExplanationValidator
{
    private readonly ExplanationValidationOptions _options;
    private readonly NumericEvidenceMatcher _numericMatcher;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExplanationValidator(ExplanationValidationOptions? options = null)
    {
        _options = options ?? ExplanationValidationOptions.Default;
        _options.Validate();
        _numericMatcher = new NumericEvidenceMatcher(_options);
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            MaxDepth = _options.MaxJsonDepth,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
        };
    }

    public ValidatedExplanationResult ValidateJson(string rawResponseJson, ExplanationValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(rawResponseJson))
        {
            return BuildStructuralFailureResult(
                context.QueryId,
                ValidationFailureCode.InvalidJson,
                "La respuesta de IA llegó vacía.");
        }

        AiExplanationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AiExplanationPayload>(rawResponseJson, _jsonOptions);
        }
        catch (JsonException ex)
        {
            return BuildStructuralFailureResult(
                context.QueryId,
                ValidationFailureCode.InvalidJson,
                $"JSON inválido o truncado al deserializar la explicación: {ex.Message}");
        }

        if (payload is null)
        {
            return BuildStructuralFailureResult(
                context.QueryId,
                ValidationFailureCode.InvalidJson,
                "La respuesta JSON se deserializó como null.");
        }

        return Validate(payload, context);
    }

    public ValidatedExplanationResult Validate(AiExplanationPayload payload, ExplanationValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);

        var failures = new List<ValidationFailure>();
        var findingsById = IndexFindings(context.Findings);
        var curatedFragments = context.CuratedFragmentIds ?? new HashSet<Guid>();
        var curatedRelations = context.CuratedCausalRelations ?? new HashSet<CuratedCausalRelation>();

        ValidateQueryId(payload.QueryId, context.QueryId, failures);

        if (_options.ValidateSummaryNumbers && !string.IsNullOrWhiteSpace(payload.Summary))
        {
            ValidateSummaryNumerics(payload.Summary, context, findingsById.Keys, failures);
        }

        var validatedClaims = new List<ValidatedClaimResult>();
        if (payload.Claims is null || payload.Claims.Count == 0)
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.EmptyClaims,
                null,
                "Estructura",
                "La explicación no contiene afirmaciones (claims) trazables."));
        }
        else
        {
            for (var index = 0; index < payload.Claims.Count; index++)
            {
                validatedClaims.Add(ValidateClaim(
                    index,
                    payload.Claims[index],
                    context,
                    findingsById,
                    curatedFragments,
                    curatedRelations,
                    failures));
            }
        }

        var status = failures.Count == 0
            ? ExplanationValidationStatus.Trazada
            : ExplanationValidationStatus.NoVerificada;

        var persistentNotice = status == ExplanationValidationStatus.Trazada
            ? ExplanationValidationConstants.PersistentNoticeText
            : null;

        return new ValidatedExplanationResult(
            status,
            status.ToContractCode(),
            persistentNotice,
            payload.QueryId?.Trim() ?? string.Empty,
            payload.Summary ?? string.Empty,
            payload.Metadata,
            validatedClaims,
            failures);
    }

    private ValidatedClaimResult ValidateClaim(
        int claimIndex,
        AiClaimPayload? claim,
        ExplanationValidationContext context,
        IReadOnlyDictionary<string, ValidatedFindingInput> findingsById,
        IReadOnlySet<Guid> curatedFragments,
        IReadOnlySet<CuratedCausalRelation> curatedRelations,
        List<ValidationFailure> failures)
    {
        var failureCountBefore = failures.Count;

        if (claim is null)
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.EmptyStatement,
                claimIndex,
                "Estructura",
                $"La afirmación #{claimIndex} es nula."));

            return new ValidatedClaimResult(
                claimIndex,
                null,
                string.Empty,
                string.Empty,
                [],
                null,
                false,
                null,
                false);
        }

        var hasValidType = ExplanationValidationConstants.TryParseClaimType(claim.ClaimType, out var claimType);
        if (!hasValidType)
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.InvalidClaimType,
                claimIndex,
                "Estructura",
                $"Tipo de afirmación inválido '{claim.ClaimType ?? string.Empty}'."));
        }

        if (string.IsNullOrWhiteSpace(claim.Statement))
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.EmptyStatement,
                claimIndex,
                "Estructura",
                $"La afirmación #{claimIndex} tiene el texto vacío."));
        }

        // Criterio (a): existencia de finding_ids
        var (normalizedFindingIds, resolvedFindings) = ValidateFindingReferences(
            claimIndex,
            claim.FindingIds,
            findingsById,
            failures);

        // Criterio (a): existencia y formato de kb_fragment_id según el tipo de afirmación
        var requiresCuratedFragment = hasValidType &&
            claimType is ExplanationClaimType.Interpretacion or ExplanationClaimType.Recomendacion;

        var parsedFragmentId = ValidateKbFragmentReference(
            claimIndex,
            claim.KbFragmentId,
            requiresCuratedFragment,
            curatedFragments,
            failures);

        // Criterio (b): concordancia numérica únicamente contra evidencia de hallazgos citados y métricas del contexto
        if (!string.IsNullOrWhiteSpace(claim.Statement))
        {
            ValidateClaimNumerics(
                claimIndex,
                claim.Statement,
                resolvedFindings,
                context.QueryMetrics,
                context.EngineVersion,
                failures);
        }

        // Criterio (c): relación causa-efecto curada para INTERPRETACION y RECOMENDACION
        if (requiresCuratedFragment && parsedFragmentId.HasValue && resolvedFindings.Count > 0)
        {
            ValidateCausalRelations(
                claimIndex,
                claimType,
                parsedFragmentId.Value,
                resolvedFindings,
                curatedRelations,
                failures);
        }

        // Etiqueta determinística de indicio (RF-IA-12, CA-HU09.4)
        var isIndicio = hasValidType &&
            claimType == ExplanationClaimType.Interpretacion &&
            resolvedFindings.Any(static finding =>
                finding.EvidenceClass is EvidenceClass.Estimada or EvidenceClass.EjecucionAgregada);

        var indicationLabel = isIndicio ? ExplanationValidationConstants.IndicationLabel : null;
        var passed = failures.Count == failureCountBefore;

        return new ValidatedClaimResult(
            claimIndex,
            hasValidType ? claimType : null,
            hasValidType ? claimType.ToContractCode() : (claim.ClaimType ?? string.Empty),
            claim.Statement ?? string.Empty,
            normalizedFindingIds,
            parsedFragmentId,
            isIndicio,
            indicationLabel,
            passed);
    }

    private static (IReadOnlyList<string> NormalizedIds, IReadOnlyList<ValidatedFindingInput> ResolvedFindings)
        ValidateFindingReferences(
            int claimIndex,
            IReadOnlyList<string>? rawFindingIds,
            IReadOnlyDictionary<string, ValidatedFindingInput> findingsById,
            List<ValidationFailure> failures)
    {
        if (rawFindingIds is null || rawFindingIds.Count == 0)
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.MissingFindingReferences,
                claimIndex,
                "Criterio (a)",
                $"La afirmación #{claimIndex} debe referenciar al menos un finding_id del contexto."));
            return ([], []);
        }

        var normalizedIds = new List<string>(rawFindingIds.Count);
        var resolvedFindings = new List<ValidatedFindingInput>(rawFindingIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawId in rawFindingIds)
        {
            var trimmed = rawId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                failures.Add(new ValidationFailure(
                    ValidationFailureCode.UnknownFindingId,
                    claimIndex,
                    "Criterio (a)",
                    $"La afirmación #{claimIndex} incluye un finding_id vacío."));
                continue;
            }

            if (!seen.Add(trimmed))
            {
                continue;
            }

            normalizedIds.Add(trimmed);
            if (findingsById.TryGetValue(trimmed, out var finding))
            {
                resolvedFindings.Add(finding);
            }
            else
            {
                failures.Add(new ValidationFailure(
                    ValidationFailureCode.UnknownFindingId,
                    claimIndex,
                    "Criterio (a)",
                    $"El hallazgo '{trimmed}' citado en la afirmación #{claimIndex} no existe en el contexto."));
            }
        }

        if (normalizedIds.Count == 0 && failures.All(f => f.ClaimIndex != claimIndex || f.Code != ValidationFailureCode.MissingFindingReferences))
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.MissingFindingReferences,
                claimIndex,
                "Criterio (a)",
                $"La afirmación #{claimIndex} no contiene identificadores de hallazgo válidos."));
        }

        return (normalizedIds, resolvedFindings);
    }

    private static Guid? ValidateKbFragmentReference(
        int claimIndex,
        string? rawFragmentId,
        bool required,
        IReadOnlySet<Guid> curatedFragments,
        List<ValidationFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(rawFragmentId))
        {
            if (required)
            {
                failures.Add(new ValidationFailure(
                    ValidationFailureCode.MissingKbFragmentForCausalClaim,
                    claimIndex,
                    "Criterio (a)",
                    $"La afirmación #{claimIndex} requiere un kb_fragment_id curado (Guid)."));
            }

            return null;
        }

        if (!Guid.TryParse(rawFragmentId.Trim(), out var fragmentId) || fragmentId == Guid.Empty)
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.InvalidKbFragmentIdFormat,
                claimIndex,
                "Criterio (a)",
                $"El kb_fragment_id '{rawFragmentId}' en la afirmación #{claimIndex} no tiene formato Guid válido."));
            return null;
        }

        if (!curatedFragments.Contains(fragmentId))
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.UnknownKbFragmentId,
                claimIndex,
                "Criterio (a)",
                $"El fragmento curado '{fragmentId}' citado en la afirmación #{claimIndex} no existe en el catálogo."));
            return null;
        }

        return fragmentId;
    }

    private void ValidateClaimNumerics(
        int claimIndex,
        string statement,
        IReadOnlyList<ValidatedFindingInput> resolvedFindings,
        IReadOnlyDictionary<string, double>? queryMetrics,
        string? engineVersion,
        List<ValidationFailure> failures)
    {
        var allowedNodes = resolvedFindings
            .SelectMany(static f => f.NodeIds ?? [])
            .Distinct()
            .ToArray();

        var knownFindingIds = resolvedFindings
            .Select(static f => f.FindingId)
            .ToArray();

        var extractedTokens = NumericEvidenceMatcher.ExtractMetricTokens(
            statement,
            allowedNodes,
            knownFindingIds,
            engineVersion);

        if (extractedTokens.Count == 0)
        {
            return;
        }

        var allowedValues = NumericEvidenceMatcher.CollectAllowedNumericValues(
            resolvedFindings,
            queryMetrics,
            engineVersion);

        var unmatched = _numericMatcher.FindUnmatchedTokens(extractedTokens, allowedValues);
        if (unmatched.Count > 0)
        {
            var rawList = string.Join(", ", unmatched.Select(static t => $"'{t.RawText}'"));
            failures.Add(new ValidationFailure(
                ValidationFailureCode.NumericMismatch,
                claimIndex,
                "Criterio (b)",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"La afirmación #{claimIndex} contiene cifras sin respaldo en la evidencia referenciada: {rawList}.")));
        }
    }

    private void ValidateSummaryNumerics(
        string summary,
        ExplanationValidationContext context,
        IEnumerable<string> knownFindingIds,
        List<ValidationFailure> failures)
    {
        var allNodeIds = context.Findings
            .SelectMany(static f => f.NodeIds ?? [])
            .Distinct()
            .ToArray();

        var extractedTokens = NumericEvidenceMatcher.ExtractMetricTokens(
            summary,
            allNodeIds,
            knownFindingIds.ToArray(),
            context.EngineVersion);

        if (extractedTokens.Count == 0)
        {
            return;
        }

        var allowedValues = NumericEvidenceMatcher.CollectAllowedNumericValues(
            context.Findings,
            context.QueryMetrics,
            context.EngineVersion);

        var unmatched = _numericMatcher.FindUnmatchedTokens(extractedTokens, allowedValues);
        if (unmatched.Count > 0)
        {
            var rawList = string.Join(", ", unmatched.Select(static t => $"'{t.RawText}'"));
            failures.Add(new ValidationFailure(
                ValidationFailureCode.NumericMismatch,
                null,
                "Criterio (b)",
                $"El resumen (summary) contiene cifras sin respaldo en la evidencia del contexto: {rawList}."));
        }
    }

    private void ValidateCausalRelations(
        int claimIndex,
        ExplanationClaimType claimType,
        Guid fragmentId,
        IReadOnlyList<ValidatedFindingInput> resolvedFindings,
        IReadOnlySet<CuratedCausalRelation> curatedRelations,
        List<ValidationFailure> failures)
    {
        var expectedRelationType = claimType switch
        {
            ExplanationClaimType.Interpretacion => CausalRelationType.Interpretacion,
            ExplanationClaimType.Recomendacion => CausalRelationType.Recomendacion,
            _ => throw new ArgumentOutOfRangeException(nameof(claimType)),
        };

        var distinctRules = resolvedFindings
            .Select(static f => f.RuleId?.Trim() ?? string.Empty)
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctRules.Length == 0)
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.UnverifiedCausalRelation,
                claimIndex,
                "Criterio (c)",
                $"La afirmación #{claimIndex} no tiene una regla asociada para validar la relación curada con '{fragmentId}'."));
            return;
        }

        if (_options.RequireAllCitedRulesForCausalRelation)
        {
            foreach (var ruleId in distinctRules)
            {
                if (!curatedRelations.Contains(new CuratedCausalRelation(ruleId, fragmentId, expectedRelationType)))
                {
                    failures.Add(new ValidationFailure(
                        ValidationFailureCode.UnverifiedCausalRelation,
                        claimIndex,
                        "Criterio (c)",
                        $"No existe relación curada ({ruleId}, {fragmentId}, {expectedRelationType}) para la afirmación #{claimIndex}."));
                }
            }
        }
        else
        {
            var anyMatches = distinctRules.Any(ruleId =>
                curatedRelations.Contains(new CuratedCausalRelation(ruleId, fragmentId, expectedRelationType)));

            if (!anyMatches)
            {
                failures.Add(new ValidationFailure(
                    ValidationFailureCode.UnverifiedCausalRelation,
                    claimIndex,
                    "Criterio (c)",
                    $"Ninguna regla citada ({string.Join(", ", distinctRules)}) tiene relación curada ({fragmentId}, {expectedRelationType}) para la afirmación #{claimIndex}."));
            }
        }
    }

    private static void ValidateQueryId(
        string? payloadQueryId,
        string expectedQueryId,
        List<ValidationFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(payloadQueryId))
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.QueryIdMismatch,
                null,
                "Criterio (a)",
                "La respuesta de IA no incluye query_id."));
            return;
        }

        if (string.IsNullOrWhiteSpace(expectedQueryId))
        {
            return;
        }

        var actualTrimmed = payloadQueryId.Trim();
        var expectedTrimmed = expectedQueryId.Trim();

        var matches = Guid.TryParse(actualTrimmed, out var actualGuid) &&
                      Guid.TryParse(expectedTrimmed, out var expectedGuid)
            ? actualGuid == expectedGuid
            : string.Equals(actualTrimmed, expectedTrimmed, StringComparison.Ordinal);

        if (!matches)
        {
            failures.Add(new ValidationFailure(
                ValidationFailureCode.QueryIdMismatch,
                null,
                "Criterio (a)",
                $"El query_id '{actualTrimmed}' no coincide con el esperado '{expectedTrimmed}'."));
        }
    }

    private static Dictionary<string, ValidatedFindingInput> IndexFindings(
        IReadOnlyList<ValidatedFindingInput>? findings)
    {
        var map = new Dictionary<string, ValidatedFindingInput>(StringComparer.Ordinal);
        if (findings is null)
        {
            return map;
        }

        foreach (var finding in findings)
        {
            if (finding is null || string.IsNullOrWhiteSpace(finding.FindingId))
            {
                continue;
            }

            map[finding.FindingId.Trim()] = finding;
        }

        return map;
    }

    private static ValidatedExplanationResult BuildStructuralFailureResult(
        string queryId,
        ValidationFailureCode code,
        string detail)
    {
        return new ValidatedExplanationResult(
            ExplanationValidationStatus.NoVerificada,
            ExplanationValidationConstants.StatusNoVerificadaCode,
            null,
            queryId,
            string.Empty,
            null,
            [],
            [new ValidationFailure(code, null, "Estructura", detail)]);
    }
}
