using QueryDoctor.Application.Engine.Rules;

namespace QueryDoctor.Application.Masking;

/// <summary>
/// Enmascarado de los campos de evidencia de los hallazgos (RNF-SEG-05). Cada clave que emiten las reglas del
/// catálogo está clasificada aquí, sin tocar las reglas: una clave nueva sin clasificar deja fuera el hallazgo
/// (fallo cerrado, D5) y la prueba guardiana del catálogo se pone en rojo.
/// </summary>
internal static class EvidenceMasker
{
    private enum FieldClass
    {
        /// <summary>Texto T-SQL del plan (predicados, expresiones): pasa por el lexer.</summary>
        SqlFragment,

        /// <summary>Identificador (DA-13 abierta: se conserva), código, enumeración o número calculado por el motor.</summary>
        Preserved,

        /// <summary>Texto libre que puede repetir el documento original: no sale nunca.</summary>
        Dropped,
    }

    private static readonly Dictionary<string, FieldClass> Policy = new(StringComparer.Ordinal)
    {
        // Fragmentos del plan con posibles literales.
        ["expression"] = FieldClass.SqlFragment,
        ["predicate"] = FieldClass.SqlFragment,

        // REG-11: el mensaje de XmlException repite parte del XML. Basta el código de failure_reason.
        ["cause"] = FieldClass.Dropped,

        // Identificadores de objetos (DA-13, deuda: seudonimización posterior).
        ["database"] = FieldClass.Preserved,
        ["schema"] = FieldClass.Preserved,
        ["table"] = FieldClass.Preserved,
        ["index"] = FieldClass.Preserved,
        ["column"] = FieldClass.Preserved,
        ["equality_columns"] = FieldClass.Preserved,
        ["inequality_columns"] = FieldClass.Preserved,
        ["include_columns"] = FieldClass.Preserved,
        ["output_columns"] = FieldClass.Preserved,

        // Códigos y enumeraciones del motor o del esquema Showplan.
        ["convert_issue"] = FieldClass.Preserved,
        ["target_data_type"] = FieldClass.Preserved,
        ["failure_reason"] = FieldClass.Preserved,
        ["has_predicate"] = FieldClass.Preserved,
        ["logical_op"] = FieldClass.Preserved,
        ["physical_op"] = FieldClass.Preserved,
        ["lookup_kind"] = FieldClass.Preserved,
        ["triggered_by"] = FieldClass.Preserved,
        ["write_cost_warning"] = FieldClass.Preserved,

        // Estimaciones y umbrales numéricos: los necesita el validador (RF-IA-03 b). No son literales de la consulta.
        ["estimate_rebinds"] = FieldClass.Preserved,
        ["estimate_rewinds"] = FieldClass.Preserved,
        ["estimated_executions"] = FieldClass.Preserved,
        ["estimated_rows"] = FieldClass.Preserved,
        ["estimated_rows_read"] = FieldClass.Preserved,
        ["estimated_subtree_cost"] = FieldClass.Preserved,
        ["impact_pct"] = FieldClass.Preserved,
        ["min_estimated_executions_threshold"] = FieldClass.Preserved,
        ["min_estimated_rows_threshold"] = FieldClass.Preserved,
        ["min_query_consumption_pct_threshold"] = FieldClass.Preserved,
        ["rows_read_to_returned_ratio"] = FieldClass.Preserved,
        ["rows_read_to_returned_ratio_threshold"] = FieldClass.Preserved,
        ["table_cardinality"] = FieldClass.Preserved,
        ["volume_rows_evaluated"] = FieldClass.Preserved,
    };

    public static bool TryMask(
        FindingContract finding,
        MaskingSession session,
        out MaskedFinding? masked,
        out MaskingFailureCause? cause)
    {
        masked = null;
        cause = null;

        var evidence = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in finding.Evidence.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var value = finding.Evidence[key];
            if (!Policy.TryGetValue(key, out var fieldClass))
            {
                cause = MaskingFailureCause.EvidenceKeyUnclassified;
                return false;
            }

            if (value is null || fieldClass == FieldClass.Dropped)
            {
                continue;
            }

            if (fieldClass == FieldClass.SqlFragment)
            {
                if (!SqlLexicalMasker.TryMask(value, session, out var maskedValue))
                {
                    cause = MaskingFailureCause.EvidenceFragmentLexicalError;
                    return false;
                }

                evidence[key] = maskedValue!;
                continue;
            }

            if (value.Contains('\'', StringComparison.Ordinal))
            {
                // Red de seguridad: un campo que debería ser identificador, código o número trae una cadena.
                cause = MaskingFailureCause.EvidenceUnexpectedLiteral;
                return false;
            }

            evidence[key] = value;
        }

        masked = new MaskedFinding(
            finding.FindingId,
            finding.Rule.Id,
            finding.Rule.Name,
            finding.Rule.Version,
            finding.Kind,
            finding.Severity,
            finding.EvidenceClass,
            finding.Nodes
                .OrderBy(n => n.NodeId)
                .ThenBy(n => n.PhysicalOp, StringComparer.Ordinal)
                .Select(n => new MaskedFindingNode(n.NodeId, n.PhysicalOp))
                .ToArray(),
            evidence);
        return true;
    }
}
