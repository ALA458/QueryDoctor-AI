using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Ingestion;

/// <summary>Configuración de la ingesta (sección Ingestion).</summary>
public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    /// <summary>Máximo de consultas por ciclo: las N de mayor duración total (RF-ING-10, D1 de S1-11).</summary>
    public int TopN { get; set; } = 100;

    /// <summary>Ventana de la PRIMERA ingesta, en horas (D7). Después se lee todo lo posterior a la marca de agua.</summary>
    public int InitialLookbackHours { get; set; } = 24;

    /// <summary>Connect Timeout y tiempo límite de cada sentencia hacia la base objetivo, en segundos.</summary>
    public int StatementTimeoutSeconds { get; set; } = 60;

    /// <summary>Límite total de una ingesta, en segundos: no retener el bloqueo por conexión indefinidamente.</summary>
    public int TimeoutSeconds { get; set; } = 600;

    public void Validate()
    {
        if (TopN is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(TopN), "Ingestion:TopN debe estar entre 1 y 5000 (RNF-RND-02).");
        }

        if (InitialLookbackHours < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialLookbackHours), "Ingestion:InitialLookbackHours debe ser al menos 1.");
        }

        if (StatementTimeoutSeconds < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(StatementTimeoutSeconds), "Ingestion:StatementTimeoutSeconds debe ser al menos 1.");
        }

        if (TimeoutSeconds < StatementTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(TimeoutSeconds), "Ingestion:TimeoutSeconds no puede ser menor que StatementTimeoutSeconds.");
        }
    }
}

/// <summary>Fila leída de Query Store: identificadores de consulta, texto y plan más la fila cruda de estadísticas.</summary>
/// <param name="QueryStoreQueryId">query_id.</param>
/// <param name="QueryStoreTextId">query_text_id.</param>
/// <param name="QueryHash">query_hash en hexadecimal.</param>
/// <param name="QueryPlanHash">query_plan_hash en hexadecimal.</param>
/// <param name="EngineVersion">engine_version con la que se compiló el plan (RNF-COM-02).</param>
/// <param name="OriginContext">Objeto que contiene la consulta (esquema.nombre), o null si es ad hoc.</param>
/// <param name="Stat">Fila cruda de estadísticas.</param>
public sealed record QueryStoreStatRow(
    long QueryStoreQueryId,
    long QueryStoreTextId,
    string? QueryHash,
    string? QueryPlanHash,
    string? EngineVersion,
    string? OriginContext,
    RuntimeStatRow Stat);

/// <summary>Parámetros de la lectura incremental.</summary>
/// <param name="AfterIntervalId">Se leen los intervalos con id mayor (la marca de agua).</param>
/// <param name="MinStartTime">Ventana de la primera ingesta; DateTimeOffset.MinValue después.</param>
/// <param name="TopCount">N del Top-N.</param>
/// <param name="IncludeTempDbAndLog">false si el perfil dice que el motor no expone esas columnas (RNF-COM-04).</param>
public sealed record QueryStoreReadRequest(long AfterIntervalId, DateTimeOffset MinStartTime, int TopCount, bool IncludeTempDbAndLog);

/// <summary>Resultado de la lectura de estadísticas.</summary>
/// <param name="Rows">Filas con tipo de ejecución conocido.</param>
/// <param name="DiscardedRows">Filas con un execution_type que no está en la documentación (se descartan con aviso).</param>
public sealed record QueryStoreReadResult(IReadOnlyList<QueryStoreStatRow> Rows, int DiscardedRows);

/// <summary>Texto o plan leído por id; Found = false si Query Store ya no lo tiene.</summary>
public sealed record QueryStoreDocument(bool Found, string? Content);

/// <summary>
/// Lecturas de la ingesta sobre la base objetivo; cada una es una sentencia de la lista blanca (RNF-SEG-01).
/// Los fallos llegan como <see cref="TelemetrySourceException"/>.
/// </summary>
public interface IQueryStoreReader
{
    /// <summary>null si sys.database_query_store_options no devolvió filas.</summary>
    Task<QueryStoreOptions?> GetOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken);

    Task<QueryStoreReadResult> ReadStatsAsync(TargetDatabaseConnection target, QueryStoreReadRequest request, CancellationToken cancellationToken);

    Task<QueryStoreDocument> GetQueryTextAsync(TargetDatabaseConnection target, long queryStoreTextId, CancellationToken cancellationToken);

    /// <summary>query_plan como nvarchar(max), nunca como xml (F8).</summary>
    Task<QueryStoreDocument> GetPlanXmlAsync(TargetDatabaseConnection target, long queryStorePlanId, CancellationToken cancellationToken);
}

/// <summary>Bloqueo por conexión: nunca dos ingestas de la misma conexión a la vez, aunque haya varios Workers.</summary>
public interface IIngestionLock
{
    /// <summary>Lo libera el Dispose; null si otra ingesta de la conexión lo tiene.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(Guid connectionId, CancellationToken cancellationToken);
}

/// <summary>Texto nuevo, ya cifrado (RNF-SEG-14). Null si Query Store ya no lo tenía.</summary>
public sealed record IngestedText(long QueryStoreTextId, string? EncryptedSqlText);

public sealed record IngestedQuery(long QueryStoreQueryId, long QueryStoreTextId, string? QueryHash, string? OriginContext);

/// <summary>Plan nuevo, con el XML ya cifrado y su clasificación (RF-ING-09).</summary>
public sealed record IngestedPlan(
    long QueryStorePlanId,
    long QueryStoreQueryId,
    string? QueryPlanHash,
    string? EngineVersion,
    string? EncryptedPlanXml,
    PlanClassification Classification);

/// <summary>
/// Todo lo que una ingesta escribe. <see cref="IIngestionStore.ApplyAsync"/> lo aplica en UNA transacción, con el
/// avance de la marca de agua y el cierre de la ejecución (RF-ING-05, RNF-FIA-01).
/// </summary>
public sealed record IngestionBatch(
    Guid ConnectionId,
    Guid RunId,
    long? ExpectedWatermark,
    long? NewWatermark,
    IReadOnlyList<IngestedText> Texts,
    IReadOnlyList<IngestedQuery> Queries,
    IReadOnlyList<IngestedPlan> Plans,
    IReadOnlyList<RuntimeStatRow> Stats,
    DateTimeOffset Now);

/// <summary>Identificadores de Query Store que ya están guardados para la conexión.</summary>
public sealed record KnownQueryStoreIds(IReadOnlySet<long> TextIds, IReadOnlySet<long> PlanIds);

/// <summary>La marca de agua cambió entre la lectura y la escritura: otra ingesta escribió (no debería, hay bloqueo).</summary>
public sealed class WatermarkConflictException : Exception
{
    public WatermarkConflictException()
        : base("La marca de agua cambió durante la ingesta.")
    {
    }

    public WatermarkConflictException(string message)
        : base(message)
    {
    }

    public WatermarkConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Persistencia de la ingesta. La implementación con EF Core y PostgreSQL vive en Infrastructure.</summary>
public interface IIngestionStore
{
    Task<Connection?> GetConnectionAsync(Guid connectionId, CancellationToken cancellationToken);

    Task<CapabilityProfile?> GetProfileAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>Ejecuciones que quedaron en Running (proceso caído) pasan a Failed/Interrupted. Se llama con el bloqueo tomado.</summary>
    Task<int> CloseInterruptedRunsAsync(Guid connectionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Crea la ejecución en Running y la confirma.</summary>
    Task<Guid> StartRunAsync(Guid connectionId, IngestionTrigger trigger, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Cierra una ejecución en Skipped o Failed con su causa. No toca datos ni la marca de agua.</summary>
    Task FinishRunAsync(Guid runId, IngestionStatus status, IngestionCause cause, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Registra una ingesta que ni siquiera empezó (ocupada o conexión inactiva).</summary>
    Task RecordSkippedAsync(Guid connectionId, IngestionTrigger trigger, IngestionCause cause, DateTimeOffset now, CancellationToken cancellationToken);

    Task<KnownQueryStoreIds> GetKnownIdsAsync(
        Guid connectionId,
        IReadOnlyCollection<long> textIds,
        IReadOnlyCollection<long> planIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Upsert idempotente por clave natural, avance de la marca de agua (solo si sigue en ExpectedWatermark) y la
    /// ejecución en Succeeded, todo en una transacción. Devuelve las filas insertadas o actualizadas.
    /// </summary>
    /// <exception cref="WatermarkConflictException">La marca ya no valía ExpectedWatermark; no se escribe nada.</exception>
    Task<int> ApplyAsync(IngestionBatch batch, CancellationToken cancellationToken);
}

/// <summary>Encola ingest_connection. El argumento es solo el identificador (§8.1).</summary>
public interface IIngestionJobScheduler
{
    void EnqueueManual(Guid connectionId);
}
