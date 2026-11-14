using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Ingestion;

/// <summary>
/// Promedios de una fila de sys.query_store_runtime_stats (RF-ING-02, RF-ING-03). Tiempos en microsegundos y memoria
/// en páginas de 8 KB, como Query Store. TempDB y bytes de log son null donde el motor no los expone (RNF-COM-04).
/// </summary>
public sealed record RuntimeMetrics(
    long ExecutionCount,
    double AvgDurationMicroseconds,
    double AvgCpuTimeMicroseconds,
    double AvgLogicalIoReads,
    double AvgPhysicalIoReads,
    double AvgLogicalIoWrites,
    double AvgRowCount,
    double AvgDop,
    double AvgMaxUsedMemoryPages,
    double? AvgTempDbSpacePages,
    double? AvgLogBytesUsed);

/// <summary>Una fila de estadísticas tal como la devuelve Query Store: puede haber varias por clave (RF-ING-04).</summary>
/// <param name="QueryStorePlanId">plan_id de Query Store.</param>
/// <param name="ExecutionType">Tipo de ejecución.</param>
/// <param name="IntervalId">runtime_stats_interval_id.</param>
/// <param name="IntervalStart">Inicio del intervalo.</param>
/// <param name="IntervalEnd">Fin del intervalo.</param>
/// <param name="IntervalClosed">El intervalo ya había terminado según el reloj del servidor al leer.</param>
/// <param name="Metrics">Promedios de la fila.</param>
public sealed record RuntimeStatRow(
    long QueryStorePlanId,
    ExecutionType ExecutionType,
    long IntervalId,
    DateTimeOffset IntervalStart,
    DateTimeOffset IntervalEnd,
    bool IntervalClosed,
    RuntimeMetrics Metrics);

/// <summary>
/// Agregación de RF-ING-04: una fila por (plan, tipo de ejecución, intervalo), con los promedios ponderados por el
/// número de ejecuciones. Función pura y determinista (mismo orden de salida para la misma entrada).
/// </summary>
public static class RuntimeStatAggregator
{
    /// <summary>Agrupa por clave natural y pondera; salida ordenada por intervalo, plan y tipo de ejecución.</summary>
    public static IReadOnlyList<RuntimeStatRow> Aggregate(IEnumerable<RuntimeStatRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .GroupBy(r => (r.QueryStorePlanId, r.ExecutionType, r.IntervalId))
            .Select(g =>
            {
                var group = g.ToList();
                var first = group[0];
                return first with
                {
                    // Si alguna fila del grupo se leyó con el intervalo abierto, el grupo se trata como abierto: así la
                    // marca de agua nunca pasa un intervalo que aún podía cambiar.
                    IntervalClosed = group.TrueForAll(r => r.IntervalClosed),
                    Metrics = Combine(group.Select(r => r.Metrics).ToList()),
                };
            })
            .OrderBy(r => r.IntervalId)
            .ThenBy(r => r.QueryStorePlanId)
            .ThenBy(r => r.ExecutionType)
            .ToList();
    }

    /// <summary>Combina las métricas de varias filas de la misma clave.</summary>
    public static RuntimeMetrics Combine(IReadOnlyList<RuntimeMetrics> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("Se necesita al menos una fila para agregar.", nameof(parts));
        }

        return new RuntimeMetrics(
            parts.Sum(p => p.ExecutionCount),
            WeightedAverage(parts, p => p.AvgDurationMicroseconds),
            WeightedAverage(parts, p => p.AvgCpuTimeMicroseconds),
            WeightedAverage(parts, p => p.AvgLogicalIoReads),
            WeightedAverage(parts, p => p.AvgPhysicalIoReads),
            WeightedAverage(parts, p => p.AvgLogicalIoWrites),
            WeightedAverage(parts, p => p.AvgRowCount),
            WeightedAverage(parts, p => p.AvgDop),
            WeightedAverage(parts, p => p.AvgMaxUsedMemoryPages),
            WeightedAverage(parts, p => p.AvgTempDbSpacePages),
            WeightedAverage(parts, p => p.AvgLogBytesUsed));
    }

    /// <summary>
    /// Σ(cᵢ·aᵢ) / Σcᵢ. Sin ejecuciones (Σcᵢ = 0) el promedio es 0: no hubo consumo. No es un promedio de promedios.
    /// </summary>
    public static double WeightedAverage(IReadOnlyList<RuntimeMetrics> parts, Func<RuntimeMetrics, double> value) =>
        WeightedAverage(parts, p => (double?)value(p)) ?? 0d;

    /// <summary>
    /// Igual, para columnas opcionales: solo pondera las filas con valor. Si ninguna tiene valor, null (la columna no
    /// existe en esa versión, RNF-COM-04); si las que tienen valor suman 0 ejecuciones, 0.
    /// </summary>
    public static double? WeightedAverage(IReadOnlyList<RuntimeMetrics> parts, Func<RuntimeMetrics, double?> value)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(value);

        double weighted = 0;
        long executions = 0;
        var anyValue = false;

        foreach (var part in parts)
        {
            if (part.ExecutionCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(parts), "count_executions no puede ser negativo.");
            }

            if (value(part) is not { } v)
            {
                continue;
            }

            anyValue = true;
            weighted += part.ExecutionCount * v;
            executions += part.ExecutionCount;
        }

        if (!anyValue)
        {
            return null;
        }

        return executions == 0 ? 0d : weighted / executions;
    }
}
