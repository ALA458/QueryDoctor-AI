using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Ingestion;

/// <summary>
/// Marca de agua de la ingesta (RF-ING-05, RNF-FIA-01, §6.2): el último runtime_stats_interval_id consolidado.
/// Función pura.
/// </summary>
public static class WatermarkCalculator
{
    /// <summary>
    /// Nueva marca: el mayor intervalo CERRADO de lo leído, sin retroceder nunca. Los intervalos aún abiertos se
    /// guardan pero no consolidan: la siguiente ingesta los vuelve a leer y el upsert los actualiza.
    /// </summary>
    public static long? Next(long? current, IEnumerable<RuntimeStatRow> aggregated)
    {
        ArgumentNullException.ThrowIfNull(aggregated);

        var maxClosed = aggregated
            .Where(r => r.IntervalClosed)
            .Select(r => (long?)r.IntervalId)
            .DefaultIfEmpty(null)
            .Max();

        if (maxClosed is null)
        {
            return current;
        }

        return current is null ? maxClosed : Math.Max(current.Value, maxClosed.Value);
    }

    /// <summary>Primer intervalo a leer: todo lo posterior a la marca (0 si nunca hubo ingesta).</summary>
    public static long AfterIntervalId(long? current) => current ?? 0;
}

/// <summary>Tipos de ejecución de sys.query_store_runtime_stats.execution_type (documentación de Microsoft).</summary>
public static class QueryStoreExecutionType
{
    /// <summary>0 = Regular, 3 = Aborted (cancelada por el cliente), 4 = Exception. Otro valor: null.</summary>
    public static ExecutionType? FromCode(int code) => code switch
    {
        0 => ExecutionType.Regular,
        3 => ExecutionType.Aborted,
        4 => ExecutionType.Exception,
        _ => null,
    };
}
