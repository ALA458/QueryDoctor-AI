namespace QueryDoctor.PerfTests;

/// <summary>Resumen de una serie de latencias en milisegundos (RNF-RND-02).</summary>
public sealed record LatencySummary(
    int Count,
    double MinMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    double MeanMs,
    double StdDevMs);

/// <summary>
/// Percentiles por el método del rango más cercano (nearest-rank): el percentil p es el valor ordenado en la posición
/// ⌈p/100 · n⌉ (base 1). Siempre devuelve una latencia que de verdad ocurrió; sin interpolar. La desviación estándar es
/// la muestral (n − 1).
/// </summary>
public static class LatencyStats
{
    public static double Percentile(IReadOnlyList<double> sortedAscending, double percentile)
    {
        ArgumentNullException.ThrowIfNull(sortedAscending);
        if (sortedAscending.Count == 0)
        {
            throw new ArgumentException("No hay muestras.", nameof(sortedAscending));
        }

        if (percentile is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), "El percentil va de (0, 100].");
        }

        var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Count);
        return sortedAscending[Math.Clamp(rank, 1, sortedAscending.Count) - 1];
    }

    public static LatencySummary Summarize(IEnumerable<double> samplesMs)
    {
        ArgumentNullException.ThrowIfNull(samplesMs);
        var sorted = samplesMs.OrderBy(x => x).ToArray();
        if (sorted.Length == 0)
        {
            return new LatencySummary(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        var mean = sorted.Average();
        var std = sorted.Length > 1 ? Math.Sqrt(sorted.Sum(x => (x - mean) * (x - mean)) / (sorted.Length - 1)) : 0.0;
        return new LatencySummary(
            sorted.Length,
            sorted[0],
            Percentile(sorted, 50),
            Percentile(sorted, 95),
            Percentile(sorted, 99),
            sorted[^1],
            mean,
            std);
    }
}
