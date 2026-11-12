using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace QueryDoctor.Domain.Analysis;

/// <summary>
/// Umbrales configurables por regla del motor determinístico con los valores por defecto documentados en el Cuadro 4.5 del SRS (RF-MOT-14, DA-02).
/// Todos los valores por defecto son provisionales (†) sujetos a calibración con el laboratorio (S3-01).
/// </summary>
public sealed record RuleThresholds
{
    public const string SectionName = "RuleThresholds";

    public const double DefaultReg03MinEstimatedRows = 10_000.0;
    public const double DefaultReg03RowsReadToReturnedRatio = 10.0;
    public const double DefaultMinQueryConsumptionPct = 1.0;
    public const double DefaultReg04MinEstimatedExecutions = 1_000.0;
    public const double DefaultReg07CardinalityRatio = 10.0;
    public const double DefaultReg08RegressionIncreasePct = 50.0;
    public const long DefaultReg08MinExecutionsPerPlan = 100L;
    public const double DefaultReg10DurationMaxMinRatio = 5.0;

    /// <summary>Instancia inmutable con todos los umbrales por defecto del Cuadro 4.5 del SRS.</summary>
    public static RuleThresholds Default { get; } = new();

    /// <summary>REG-03: Filas estimadas mínimas para señalar un recorrido completo por volumen (por defecto 10 000 †).</summary>
    public double Reg03MinEstimatedRows { get; init; } = DefaultReg03MinEstimatedRows;

    /// <summary>REG-03: Razón mínima entre filas leídas estimadas y filas devueltas estimadas cuando hay predicado (por defecto 10 †).</summary>
    public double Reg03RowsReadToReturnedRatio { get; init; } = DefaultReg03RowsReadToReturnedRatio;

    /// <summary>REG-03 / REG-04: Porcentaje mínimo de consumo de la consulta en la ventana de análisis, en escala 0..100 (por defecto 1 % †).</summary>
    public double MinQueryConsumptionPct { get; init; } = DefaultMinQueryConsumptionPct;

    /// <summary>REG-04: Número mínimo de ejecuciones estimadas del operador Key Lookup / RID Lookup (por defecto 1 000 †).</summary>
    public double Reg04MinEstimatedExecutions { get; init; } = DefaultReg04MinEstimatedExecutions;

    /// <summary>REG-07: Razón mínima entre filas estimadas de la sentencia y filas devueltas promedio en cualquier sentido (por defecto 10 †).</summary>
    public double Reg07CardinalityRatio { get; init; } = DefaultReg07CardinalityRatio;

    /// <summary>REG-08: Porcentaje mínimo de aumento de duración o CPU promedio ponderado del plan reciente respecto al anterior (por defecto +50 % †).</summary>
    public double Reg08RegressionIncreasePct { get; init; } = DefaultReg08RegressionIncreasePct;

    /// <summary>REG-08: Número mínimo de ejecuciones por plan para evaluar regresión entre planes (por defecto 100 †).</summary>
    public long Reg08MinExecutionsPerPlan { get; init; } = DefaultReg08MinExecutionsPerPlan;

    /// <summary>REG-10: Razón mínima entre la duración promedio máxima y mínima entre planes compilados de la misma consulta (por defecto 5 †).</summary>
    public double Reg10DurationMaxMinRatio { get; init; } = DefaultReg10DurationMaxMinRatio;

    /// <summary>
    /// Devuelve los umbrales efectivos aplicables a una regla específica (<paramref name="ruleId"/>) como diccionario ordenado por clave (RF-MOT-14, RF-MOT-17).
    /// Las reglas que no usan umbrales numéricos (por ejemplo REG-01, REG-02, REG-05, REG-06, REG-11) devuelven un diccionario vacío.
    /// </summary>
    public IReadOnlyDictionary<string, double> GetThresholdsForRule(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        return ruleId.ToUpperInvariant() switch
        {
            "REG-03" => new SortedDictionary<string, double>(StringComparer.Ordinal)
            {
                ["min_estimated_rows"] = Reg03MinEstimatedRows,
                ["min_query_consumption_pct"] = MinQueryConsumptionPct,
                ["rows_read_to_returned_ratio"] = Reg03RowsReadToReturnedRatio,
            },
            "REG-04" => new SortedDictionary<string, double>(StringComparer.Ordinal)
            {
                ["min_estimated_executions"] = Reg04MinEstimatedExecutions,
                ["min_query_consumption_pct"] = MinQueryConsumptionPct,
            },
            "REG-07" => new SortedDictionary<string, double>(StringComparer.Ordinal)
            {
                ["cardinality_ratio"] = Reg07CardinalityRatio,
            },
            "REG-08" => new SortedDictionary<string, double>(StringComparer.Ordinal)
            {
                ["min_executions_per_plan"] = Reg08MinExecutionsPerPlan,
                ["regression_increase_pct"] = Reg08RegressionIncreasePct,
            },
            "REG-10" => new SortedDictionary<string, double>(StringComparer.Ordinal)
            {
                ["duration_max_min_ratio"] = Reg10DurationMaxMinRatio,
            },
            _ => new SortedDictionary<string, double>(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Devuelve todos los parámetros de umbral como diccionario ordenado canónico.
    /// </summary>
    public IReadOnlyDictionary<string, double> ToDictionary() =>
        new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["min_query_consumption_pct"] = MinQueryConsumptionPct,
            ["reg03_min_estimated_rows"] = Reg03MinEstimatedRows,
            ["reg03_rows_read_to_returned_ratio"] = Reg03RowsReadToReturnedRatio,
            ["reg04_min_estimated_executions"] = Reg04MinEstimatedExecutions,
            ["reg07_cardinality_ratio"] = Reg07CardinalityRatio,
            ["reg08_min_executions_per_plan"] = Reg08MinExecutionsPerPlan,
            ["reg08_regression_increase_pct"] = Reg08RegressionIncreasePct,
            ["reg10_duration_max_min_ratio"] = Reg10DurationMaxMinRatio,
        };

    /// <summary>
    /// Representación JSON canónica (claves ordenadas, sin espacios, cultura invariante) para persistencia en <c>analysis.threshold_set</c> y <c>analysis.analysis_run</c>.
    /// </summary>
    public string ToCanonicalJson() => SerializeDictionaryToCanonicalJson(ToDictionary());

    /// <summary>
    /// Calcula el hash SHA-256 en hexadecimal (64 caracteres en mayúsculas) sobre el JSON canónico de los umbrales (RF-MOT-02, RF-MOT-14).
    /// </summary>
    public string ComputeConfigurationHash()
    {
        var bytes = Encoding.UTF8.GetBytes(ToCanonicalJson());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Serializa de forma determinista cualquier diccionario de umbrales a JSON canónico con claves ordenadas.
    /// </summary>
    public static string SerializeDictionaryToCanonicalJson(IReadOnlyDictionary<string, double> thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        if (thresholds.Count == 0)
        {
            return "{}";
        }

        var builder = new StringBuilder();
        builder.Append('{');
        var first = true;
        foreach (var pair in thresholds.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append('"')
                .Append(pair.Key)
                .Append("\":")
                .Append(pair.Value.ToString("G17", CultureInfo.InvariantCulture));
        }

        builder.Append('}');
        return builder.ToString();
    }
}
