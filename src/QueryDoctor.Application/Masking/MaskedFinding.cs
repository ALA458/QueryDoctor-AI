namespace QueryDoctor.Application.Masking;

/// <summary>
/// Hallazgo listo para salir hacia la IA: datos del catálogo y del motor más la evidencia enmascarada
/// (RNF-SEG-05). Solo el enmascarador puede construirlo.
/// </summary>
public sealed class MaskedFinding
{
    internal MaskedFinding(
        string findingId,
        string ruleId,
        string ruleName,
        string ruleVersion,
        string kind,
        string severity,
        string evidenceClass,
        IReadOnlyList<MaskedFindingNode> nodes,
        IReadOnlyDictionary<string, string> evidence)
    {
        FindingId = findingId;
        RuleId = ruleId;
        RuleName = ruleName;
        RuleVersion = ruleVersion;
        Kind = kind;
        Severity = severity;
        EvidenceClass = evidenceClass;
        Nodes = nodes;
        Evidence = evidence;
    }

    /// <summary>Identificador determinista del hallazgo (hash del motor).</summary>
    public string FindingId { get; }

    /// <summary>Identificador de la regla, por ejemplo <c>REG-01</c>.</summary>
    public string RuleId { get; }

    /// <summary>Nombre de la regla en el catálogo.</summary>
    public string RuleName { get; }

    /// <summary>Versión de la regla.</summary>
    public string RuleVersion { get; }

    /// <summary>Tipo de hallazgo del contrato (<c>SENAL</c>, <c>PLAN_NO_ANALIZABLE</c>…).</summary>
    public string Kind { get; }

    /// <summary>Severidad del contrato.</summary>
    public string Severity { get; }

    /// <summary>Clase de evidencia del contrato.</summary>
    public string EvidenceClass { get; }

    /// <summary>Nodos del plan señalados.</summary>
    public IReadOnlyList<MaskedFindingNode> Nodes { get; }

    /// <summary>Evidencia enmascarada, con claves en orden ordinal.</summary>
    public IReadOnlyDictionary<string, string> Evidence { get; }
}

/// <summary>Nodo del plan señalado por un hallazgo.</summary>
public sealed record MaskedFindingNode(int NodeId, string PhysicalOp);

/// <summary>
/// Hallazgo que quedó fuera del contexto por fallo cerrado. Solo lleva identificadores y la causa.
/// </summary>
public sealed record MaskingExclusion(string FindingId, string RuleId, MaskingFailureCause Cause);
