namespace QueryDoctor.Domain.Core;

/// <summary>
/// Estadística de ejecución agregada por plan, tipo de ejecución e intervalo (RF-ING-02 a RF-ING-05).
/// Clave natural: (ConnectionId, PlanId, ExecutionType, IntervalId); hace idempotente el upsert.
/// Tabla core.runtime_stat. Los tiempos están en microsegundos, como en Query Store.
/// </summary>
public sealed class RuntimeStat
{
    public long Id { get; set; }

    public Guid ConnectionId { get; set; }

    public Guid PlanId { get; set; }

    public ExecutionType ExecutionType { get; set; }

    /// <summary>runtime_stats_interval_id en Query Store.</summary>
    public long IntervalId { get; set; }

    public DateTimeOffset IntervalStart { get; set; }

    public DateTimeOffset IntervalEnd { get; set; }

    public long ExecutionCount { get; set; }

    public double AvgDurationMicroseconds { get; set; }

    public double AvgCpuTimeMicroseconds { get; set; }

    public double AvgLogicalIoReads { get; set; }

    public double AvgPhysicalIoReads { get; set; }

    public double AvgLogicalIoWrites { get; set; }

    public double AvgRowCount { get; set; }

    public double AvgDop { get; set; }

    public double AvgMaxUsedMemoryPages { get; set; }

    /// <summary>Solo SQL Server 2017+ y Azure SQL (RF-ING-03).</summary>
    public double? AvgTempDbSpacePages { get; set; }

    /// <summary>Solo SQL Server 2017+ y Azure SQL (RF-ING-03).</summary>
    public double? AvgLogBytesUsed { get; set; }

    public DateTimeOffset IngestedAt { get; set; }
}
