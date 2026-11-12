using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.Application.Engine.Rules;

/// <summary>
/// Contrato de datos JSON versionado de un hallazgo del motor determinístico (RF-MOT-03, RF-MOT-17, Apéndice B Listing B.1).
/// </summary>
public sealed record FindingContract(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("finding_id")] string FindingId,
    [property: JsonPropertyName("catalog_version")] string CatalogVersion,
    [property: JsonPropertyName("rule")] FindingRuleContract Rule,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("evidence_class")] string EvidenceClass,
    [property: JsonPropertyName("query_id")] long QueryId,
    [property: JsonPropertyName("plan_id")] long PlanId,
    [property: JsonPropertyName("nodes")] IReadOnlyList<FindingNodeContract> Nodes,
    [property: JsonPropertyName("evidence")] IReadOnlyDictionary<string, string?> Evidence,
    [property: JsonPropertyName("context")] FindingContextContract Context,
    [property: JsonPropertyName("thresholds_used")] IReadOnlyDictionary<string, double> ThresholdsUsed,
    [property: JsonPropertyName("engine_version")] string? EngineVersion,
    [property: JsonPropertyName("analyzed_at")] DateTimeOffset AnalyzedAt)
{
    /// <summary>Versión actual del esquema del contrato JSON del hallazgo (RF-MOT-17).</summary>
    public const string CurrentSchemaVersion = "1.0.0";

    /// <summary>
    /// Serializa este hallazgo a su representación JSON canónica conforme al contrato versionado (RF-MOT-17).
    /// </summary>
    public string ToJson(bool indented = false) =>
        FindingContractJson.Serialize(this, indented);
}

/// <summary>
/// Metadatos de la regla embebidos dentro del contrato JSON del hallazgo (RF-MOT-03, Apéndice B Listing B.1).
/// </summary>
public sealed record FindingRuleContract(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version);

/// <summary>
/// Referencia a un nodo del plan de ejecución involucrado en un hallazgo (RF-MOT-03, Apéndice B Listing B.1).
/// </summary>
public sealed record FindingNodeContract(
    [property: JsonPropertyName("node_id")] int NodeId,
    [property: JsonPropertyName("physical_op")] string PhysicalOp);

/// <summary>
/// Datos de contexto empleados al evaluar el hallazgo (RF-MOT-03, Apéndice B Listing B.1).
/// </summary>
public sealed record FindingContextContract(
    [property: JsonPropertyName("executions")] long? Executions = null,
    [property: JsonPropertyName("query_consumption_pct")] double? QueryConsumptionPct = null,
    [property: JsonPropertyName("estimated_rows")] double? EstimatedRows = null,
    [property: JsonPropertyName("estimated_rows_read")] double? EstimatedRowsRead = null,
    [property: JsonPropertyName("avg_rows_returned")] double? AvgRowsReturned = null);

/// <summary>
/// Serializador determinista para el contrato JSON del hallazgo (<see cref="FindingContract"/>).
/// </summary>
public static class FindingContractJson
{
    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(FindingContract contract, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var normalized = NormalizeDictionaries(contract);
        return JsonSerializer.Serialize(normalized, indented ? IndentedOptions : CompactOptions);
    }

    public static string SerializeList(IReadOnlyList<FindingContract> contracts, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var normalized = contracts.Select(NormalizeDictionaries).ToList();
        return JsonSerializer.Serialize(normalized, indented ? IndentedOptions : CompactOptions);
    }

    public static FindingContract? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize<FindingContract>(json, CompactOptions);
    }

    public static string SerializeNodes(IReadOnlyList<FindingNodeContract> nodes) =>
        JsonSerializer.Serialize(nodes, CompactOptions);

    public static string SerializeEvidence(IReadOnlyDictionary<string, string?> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var sorted = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in evidence)
        {
            sorted[pair.Key] = pair.Value;
        }

        return JsonSerializer.Serialize(sorted, CompactOptions);
    }

    public static string SerializeContext(FindingContextContract context) =>
        JsonSerializer.Serialize(context, CompactOptions);

    private static FindingContract NormalizeDictionaries(FindingContract contract)
    {
        var sortedEvidence = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var pair in contract.Evidence)
        {
            sortedEvidence[pair.Key] = pair.Value;
        }

        var sortedThresholds = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in contract.ThresholdsUsed)
        {
            sortedThresholds[pair.Key] = pair.Value;
        }

        var sortedNodes = contract.Nodes
            .OrderBy(n => n.NodeId)
            .ThenBy(n => n.PhysicalOp, StringComparer.Ordinal)
            .ToArray();

        return contract with
        {
            Nodes = sortedNodes,
            Evidence = sortedEvidence,
            ThresholdsUsed = sortedThresholds,
        };
    }
}
