namespace QueryDoctor.Domain.Analysis;

/// <summary>
/// Puntaje compuesto de salud <c>H(q)</c> e índice de prioridad de revisión <c>P(q)</c> calculado para una consulta
/// en una ventana de análisis y persistido tras cada corrida de ingesta (RF-SCO-01..06, E-07). Tabla <c>analysis.health_score</c>.
/// </summary>
public sealed class HealthScore
{
    public Guid Id { get; set; }

    public Guid ConnectionId { get; set; }

    public Guid QueryId { get; set; }

    /// <summary>Corrida de ingesta tras la cual se calculó este puntaje (RF-SCO-05).</summary>
    public Guid? IngestionRunId { get; set; }

    /// <summary>Inicio de la ventana de análisis W evaluada.</summary>
    public DateTimeOffset WindowStart { get; set; }

    /// <summary>Fin de la ventana de análisis W evaluada.</summary>
    public DateTimeOffset WindowEnd { get; set; }

    /// <summary>Puntaje de salud compuesto <c>H(q) = 100 - P(q)</c> en la escala 0 a 100 (100 es saludable, RF-SCO-01, Ecuación 4.5).</summary>
    public double Score { get; set; }

    /// <summary>Índice de prioridad de revisión <c>P(q)</c> en la escala 0 a 100 (Ecuación 4.4).</summary>
    public double Priority { get; set; }

    /// <summary>Banda de interpretación normativa: Crítica (0–39), Atención (40–69), Saludable (70–100) (RF-SCO-06, CA-HU08.3).</summary>
    public HealthScoreBand Band { get; set; } = HealthScoreBand.Saludable;

    /// <summary>Componente de impacto en la carga <c>I(q)</c> en [0, 1] (Ecuación 4.2).</summary>
    public double ImpactComponent { get; set; }

    /// <summary>Componente de severidad de hallazgos <c>S(q)</c> en [0, 1] (Ecuación 4.3).</summary>
    public double SeverityComponent { get; set; }

    /// <summary>Parámetro de prioridad base por consumo <c>alpha</c> empleado en el cálculo (Ecuación 4.4).</summary>
    public double AlphaUsed { get; set; }

    /// <summary>Participación relativa de la consulta en el consumo total de CPU de la ventana <c>s_CPU(q)</c> en [0, 1] (Ecuación 4.1).</summary>
    public double CpuShare { get; set; }

    /// <summary>Participación relativa de la consulta en la duración total de la ventana <c>s_DUR(q)</c> en [0, 1] (Ecuación 4.1).</summary>
    public double DurationShare { get; set; }

    /// <summary>Participación relativa de la consulta en las lecturas lógicas totales de la ventana <c>s_LECT(q)</c> en [0, 1] (Ecuación 4.1).</summary>
    public double LogicalReadsShare { get; set; }

    /// <summary>
    /// Porcentaje de consumo ponderado de la consulta en la ventana en escala 0 a 100:
    /// <c>(w_CPU * s_CPU + w_DUR * s_DUR + w_LECT * s_LECT) * 100</c> (RF-SCO-03, RF-SCO-04).
    /// </summary>
    public double WeightedConsumptionPct { get; set; }

    /// <summary>Total de ejecuciones acumuladas de la consulta en la ventana W.</summary>
    public long TotalExecutions { get; set; }

    /// <summary>Consumo acumulado de CPU <c>C_CPU(q)</c> en microsegundos durante la ventana W (Ecuación 4.1).</summary>
    public double TotalCpuMicroseconds { get; set; }

    /// <summary>Consumo acumulado de duración <c>C_DUR(q)</c> en microsegundos durante la ventana W (Ecuación 4.1).</summary>
    public double TotalDurationMicroseconds { get; set; }

    /// <summary>Consumo acumulado de lecturas lógicas <c>C_LECT(q)</c> durante la ventana W (Ecuación 4.1).</summary>
    public double TotalLogicalReads { get; set; }

    /// <summary>Número de hallazgos determinísticos considerados en <c>S(q)</c>.</summary>
    public int FindingsCount { get; set; }

    /// <summary>
    /// Desglose JSON de los hallazgos que contribuyeron a <c>S(q)</c> con su regla, severidad, clase de evidencia y aporte <c>sev_i * conf_i</c> (RF-SCO-03, E-07).
    /// </summary>
    public string ContributingFindingsJson { get; set; } = "[]";

    /// <summary>
    /// Snapshot JSON canónico de los parámetros (<see cref="HealthScoreParameters"/>) empleados en este cálculo (RF-SCO-02, E-07).
    /// </summary>
    public string ParametersJson { get; set; } = "{}";

    /// <summary>
    /// Hash SHA-256 en hexadecimal (64 caracteres) sobre <see cref="ParametersJson"/> para trazabilidad (RF-SCO-02).
    /// </summary>
    public string ParametersHash { get; set; } = string.Empty;

    /// <summary>Instante en que se registró el cálculo del puntaje (RF-SCO-05, E-07).</summary>
    public DateTimeOffset CalculatedAt { get; set; }
}
