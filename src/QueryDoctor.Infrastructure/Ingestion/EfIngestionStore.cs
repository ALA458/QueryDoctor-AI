using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Domain.Core;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.Infrastructure.Ingestion;

/// <summary>
/// Persistencia de la ingesta con EF Core sobre PostgreSQL (S1-11). El upsert usa INSERT ... ON CONFLICT sobre las
/// claves naturales (RF-ING-05) con arreglos y unnest: una sentencia por tabla, sin N+1. Todo lo de
/// <see cref="ApplyAsync"/> va en UNA transacción: si algo falla, ni los datos ni la marca de agua cambian
/// (RNF-FIA-01, CA-HU03.4). Las sentencias pasan por EF (ExecuteSqlRaw) para usar la transacción y los interceptores.
/// </summary>
public sealed class EfIngestionStore : IIngestionStore
{
    private readonly AppDbContext _db;

    public EfIngestionStore(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public Task<Connection?> GetConnectionAsync(Guid connectionId, CancellationToken cancellationToken) =>
        _db.Connections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);

    public Task<CapabilityProfile?> GetProfileAsync(Guid connectionId, CancellationToken cancellationToken) =>
        _db.CapabilityProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.ConnectionId == connectionId, cancellationToken);

    public Task<int> CloseInterruptedRunsAsync(Guid connectionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        _db.IngestionRuns
            .Where(r => r.ConnectionId == connectionId && r.Status == IngestionStatus.Running)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, IngestionStatus.Failed)
                    .SetProperty(r => r.ErrorCause, nameof(IngestionCause.Interrupted))
                    .SetProperty(r => r.FinishedAt, now),
                cancellationToken);

    public async Task<Guid> StartRunAsync(Guid connectionId, IngestionTrigger trigger, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = new IngestionRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            Trigger = trigger,
            Status = IngestionStatus.Running,
            StartedAt = now,
        };

        await AddAndSaveAsync(run, cancellationToken);
        return run.Id;
    }

    public Task FinishRunAsync(Guid runId, IngestionStatus status, IngestionCause cause, DateTimeOffset now, CancellationToken cancellationToken) =>
        _db.IngestionRuns
            .Where(r => r.Id == runId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, status)
                    .SetProperty(r => r.ErrorCause, cause.ToString())
                    .SetProperty(r => r.FinishedAt, now),
                cancellationToken);

    public Task RecordSkippedAsync(Guid connectionId, IngestionTrigger trigger, IngestionCause cause, DateTimeOffset now, CancellationToken cancellationToken) =>
        AddAndSaveAsync(
            new IngestionRun
            {
                Id = Guid.NewGuid(),
                ConnectionId = connectionId,
                Trigger = trigger,
                Status = IngestionStatus.Skipped,
                StartedAt = now,
                FinishedAt = now,
                ErrorCause = cause.ToString(),
            },
            cancellationToken);

    public async Task<KnownQueryStoreIds> GetKnownIdsAsync(
        Guid connectionId,
        IReadOnlyCollection<long> textIds,
        IReadOnlyCollection<long> planIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(textIds);
        ArgumentNullException.ThrowIfNull(planIds);

        var texts = await _db.QueryTexts.AsNoTracking()
            .Where(t => t.ConnectionId == connectionId && textIds.Contains(t.QueryStoreTextId))
            .Select(t => t.QueryStoreTextId)
            .ToListAsync(cancellationToken);

        var plans = await _db.Plans.AsNoTracking()
            .Where(p => p.ConnectionId == connectionId && planIds.Contains(p.QueryStorePlanId))
            .Select(p => p.QueryStorePlanId)
            .ToListAsync(cancellationToken);

        return new KnownQueryStoreIds(texts.ToHashSet(), plans.ToHashSet());
    }

    public async Task<int> ApplyAsync(IngestionBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var rows = 0;
        rows += await UpsertTextsAsync(batch, cancellationToken);
        rows += await UpsertQueriesAsync(batch, cancellationToken);
        rows += await UpsertPlansAsync(batch, cancellationToken);
        rows += await UpsertRuntimeStatsAsync(batch, cancellationToken);

        // Compare-and-set: la marca solo avanza si sigue en el valor con el que se leyó (el bloqueo ya lo garantiza;
        // esto protege si alguien lo saltara). updated_at solo cambia si la marca cambia.
        var watermarkRows = await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE core.connection
            SET last_consolidated_interval_id = @new_watermark,
                updated_at = CASE WHEN last_consolidated_interval_id IS DISTINCT FROM @new_watermark THEN @now ELSE updated_at END
            WHERE id = @connection_id AND last_consolidated_interval_id IS NOT DISTINCT FROM @expected_watermark
            """,
            [
                Param("new_watermark", batch.NewWatermark, NpgsqlDbType.Bigint),
                Param("expected_watermark", batch.ExpectedWatermark, NpgsqlDbType.Bigint),
                Param("now", batch.Now, NpgsqlDbType.TimestampTz),
                Param("connection_id", batch.ConnectionId, NpgsqlDbType.Uuid),
            ],
            cancellationToken);

        if (watermarkRows != 1)
        {
            throw new WatermarkConflictException();
        }

        await _db.Database.ExecuteSqlRawAsync(
            """
            UPDATE core.ingestion_run
            SET status = @status, finished_at = @now, rows_upserted = @rows, error_cause = NULL
            WHERE id = @run_id
            """,
            [
                Param("status", nameof(IngestionStatus.Succeeded), NpgsqlDbType.Varchar),
                Param("now", batch.Now, NpgsqlDbType.TimestampTz),
                Param("rows", rows, NpgsqlDbType.Integer),
                Param("run_id", batch.RunId, NpgsqlDbType.Uuid),
            ],
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    // Textos: solo inserción. En Query Store no cambian y volver a cifrarlos (nonce aleatorio) cambiaría el valor.
    private Task<int> UpsertTextsAsync(IngestionBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Texts.Count == 0)
        {
            return Task.FromResult(0);
        }

        return _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO core.query_text (id, connection_id, query_store_text_id, encrypted_sql_text, ingested_at)
            SELECT gen_random_uuid(), @connection_id, t.text_id, t.sql_text, @now
            FROM unnest(@text_ids, @sql_texts) AS t(text_id, sql_text)
            ON CONFLICT (connection_id, query_store_text_id) DO NOTHING
            """,
            [
                Param("connection_id", batch.ConnectionId, NpgsqlDbType.Uuid),
                Param("now", batch.Now, NpgsqlDbType.TimestampTz),
                Array("text_ids", batch.Texts.Select(t => t.QueryStoreTextId).ToArray(), NpgsqlDbType.Bigint),
                Array("sql_texts", batch.Texts.Select(t => t.EncryptedSqlText).ToArray(), NpgsqlDbType.Text),
            ],
            cancellationToken);
    }

    private Task<int> UpsertQueriesAsync(IngestionBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Queries.Count == 0)
        {
            return Task.FromResult(0);
        }

        return _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO core.query (id, connection_id, query_store_query_id, query_text_id, query_hash, origin_context, ingested_at)
            SELECT gen_random_uuid(), @connection_id, t.query_id, qt.id, t.query_hash, t.origin_context, @now
            FROM unnest(@query_ids, @text_ids, @query_hashes, @origin_contexts) AS t(query_id, text_id, query_hash, origin_context)
            LEFT JOIN core.query_text qt ON qt.connection_id = @connection_id AND qt.query_store_text_id = t.text_id
            ON CONFLICT (connection_id, query_store_query_id) DO NOTHING
            """,
            [
                Param("connection_id", batch.ConnectionId, NpgsqlDbType.Uuid),
                Param("now", batch.Now, NpgsqlDbType.TimestampTz),
                Array("query_ids", batch.Queries.Select(q => q.QueryStoreQueryId).ToArray(), NpgsqlDbType.Bigint),
                Array("text_ids", batch.Queries.Select(q => q.QueryStoreTextId).ToArray(), NpgsqlDbType.Bigint),
                Array("query_hashes", batch.Queries.Select(q => q.QueryHash).ToArray(), NpgsqlDbType.Varchar),
                Array("origin_contexts", batch.Queries.Select(q => q.OriginContext).ToArray(), NpgsqlDbType.Varchar),
            ],
            cancellationToken);
    }

    // Planes: solo inserción, con su clasificación (RF-ING-09). Un plan ya guardado no se vuelve a leer ni a cifrar.
    private Task<int> UpsertPlansAsync(IngestionBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Plans.Count == 0)
        {
            return Task.FromResult(0);
        }

        return _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO core.plan (id, connection_id, query_id, query_store_plan_id, query_plan_hash, encrypted_plan_xml,
                                   engine_version, analysis_status, not_analyzable_cause, ingested_at)
            SELECT gen_random_uuid(), @connection_id, q.id, t.plan_id, t.plan_hash, t.plan_xml, t.engine_version, t.status, t.cause, @now
            FROM unnest(@plan_ids, @query_ids, @plan_hashes, @plan_xmls, @engine_versions, @statuses, @causes)
                 AS t(plan_id, query_id, plan_hash, plan_xml, engine_version, status, cause)
            JOIN core.query q ON q.connection_id = @connection_id AND q.query_store_query_id = t.query_id
            ON CONFLICT (connection_id, query_store_plan_id) DO NOTHING
            """,
            [
                Param("connection_id", batch.ConnectionId, NpgsqlDbType.Uuid),
                Param("now", batch.Now, NpgsqlDbType.TimestampTz),
                Array("plan_ids", batch.Plans.Select(p => p.QueryStorePlanId).ToArray(), NpgsqlDbType.Bigint),
                Array("query_ids", batch.Plans.Select(p => p.QueryStoreQueryId).ToArray(), NpgsqlDbType.Bigint),
                Array("plan_hashes", batch.Plans.Select(p => p.QueryPlanHash).ToArray(), NpgsqlDbType.Varchar),
                Array("plan_xmls", batch.Plans.Select(p => p.EncryptedPlanXml).ToArray(), NpgsqlDbType.Text),
                Array("engine_versions", batch.Plans.Select(p => p.EngineVersion).ToArray(), NpgsqlDbType.Varchar),
                Array("statuses", batch.Plans.Select(p => p.Classification.Status.ToString()).ToArray(), NpgsqlDbType.Varchar),
                Array("causes", batch.Plans.Select(p => p.Classification.Cause?.ToString()).ToArray(), NpgsqlDbType.Varchar),
            ],
            cancellationToken);
    }

    // Estadísticas: upsert por (connection_id, plan_id, execution_type, interval_id). El intervalo activo se vuelve a
    // leer en cada ingesta y se actualiza; si los valores no cambiaron, la fila no se toca (ni ingested_at).
    private Task<int> UpsertRuntimeStatsAsync(IngestionBatch batch, CancellationToken cancellationToken)
    {
        if (batch.Stats.Count == 0)
        {
            return Task.FromResult(0);
        }

        var s = batch.Stats;
        return _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO core.runtime_stat (connection_id, plan_id, execution_type, interval_id, interval_start, interval_end,
                execution_count, avg_duration_microseconds, avg_cpu_time_microseconds, avg_logical_io_reads,
                avg_physical_io_reads, avg_logical_io_writes, avg_row_count, avg_dop, avg_max_used_memory_pages,
                avg_temp_db_space_pages, avg_log_bytes_used, ingested_at)
            SELECT @connection_id, p.id, t.execution_type, t.interval_id, t.interval_start, t.interval_end,
                t.execution_count, t.duration, t.cpu, t.logical_reads, t.physical_reads, t.logical_writes, t.row_count,
                t.dop, t.memory, t.tempdb, t.log_bytes, @now
            FROM unnest(@plan_ids, @execution_types, @interval_ids, @interval_starts, @interval_ends, @execution_counts,
                        @durations, @cpus, @logical_reads, @physical_reads, @logical_writes, @row_counts, @dops,
                        @memories, @tempdbs, @log_bytes)
                 AS t(plan_id, execution_type, interval_id, interval_start, interval_end, execution_count, duration, cpu,
                      logical_reads, physical_reads, logical_writes, row_count, dop, memory, tempdb, log_bytes)
            JOIN core.plan p ON p.connection_id = @connection_id AND p.query_store_plan_id = t.plan_id
            ON CONFLICT (connection_id, plan_id, execution_type, interval_id) DO UPDATE SET
                interval_start = EXCLUDED.interval_start,
                interval_end = EXCLUDED.interval_end,
                execution_count = EXCLUDED.execution_count,
                avg_duration_microseconds = EXCLUDED.avg_duration_microseconds,
                avg_cpu_time_microseconds = EXCLUDED.avg_cpu_time_microseconds,
                avg_logical_io_reads = EXCLUDED.avg_logical_io_reads,
                avg_physical_io_reads = EXCLUDED.avg_physical_io_reads,
                avg_logical_io_writes = EXCLUDED.avg_logical_io_writes,
                avg_row_count = EXCLUDED.avg_row_count,
                avg_dop = EXCLUDED.avg_dop,
                avg_max_used_memory_pages = EXCLUDED.avg_max_used_memory_pages,
                avg_temp_db_space_pages = EXCLUDED.avg_temp_db_space_pages,
                avg_log_bytes_used = EXCLUDED.avg_log_bytes_used,
                ingested_at = EXCLUDED.ingested_at
            WHERE (runtime_stat.interval_start, runtime_stat.interval_end, runtime_stat.execution_count,
                   runtime_stat.avg_duration_microseconds, runtime_stat.avg_cpu_time_microseconds,
                   runtime_stat.avg_logical_io_reads, runtime_stat.avg_physical_io_reads,
                   runtime_stat.avg_logical_io_writes, runtime_stat.avg_row_count, runtime_stat.avg_dop,
                   runtime_stat.avg_max_used_memory_pages, runtime_stat.avg_temp_db_space_pages,
                   runtime_stat.avg_log_bytes_used)
                  IS DISTINCT FROM
                  (EXCLUDED.interval_start, EXCLUDED.interval_end, EXCLUDED.execution_count,
                   EXCLUDED.avg_duration_microseconds, EXCLUDED.avg_cpu_time_microseconds,
                   EXCLUDED.avg_logical_io_reads, EXCLUDED.avg_physical_io_reads,
                   EXCLUDED.avg_logical_io_writes, EXCLUDED.avg_row_count, EXCLUDED.avg_dop,
                   EXCLUDED.avg_max_used_memory_pages, EXCLUDED.avg_temp_db_space_pages,
                   EXCLUDED.avg_log_bytes_used)
            """,
            [
                Param("connection_id", batch.ConnectionId, NpgsqlDbType.Uuid),
                Param("now", batch.Now, NpgsqlDbType.TimestampTz),
                Array("plan_ids", s.Select(r => r.QueryStorePlanId).ToArray(), NpgsqlDbType.Bigint),
                Array("execution_types", s.Select(r => r.ExecutionType.ToString()).ToArray(), NpgsqlDbType.Varchar),
                Array("interval_ids", s.Select(r => r.IntervalId).ToArray(), NpgsqlDbType.Bigint),
                Array("interval_starts", s.Select(r => r.IntervalStart.ToUniversalTime()).ToArray(), NpgsqlDbType.TimestampTz),
                Array("interval_ends", s.Select(r => r.IntervalEnd.ToUniversalTime()).ToArray(), NpgsqlDbType.TimestampTz),
                Array("execution_counts", s.Select(r => r.Metrics.ExecutionCount).ToArray(), NpgsqlDbType.Bigint),
                Array("durations", s.Select(r => r.Metrics.AvgDurationMicroseconds).ToArray(), NpgsqlDbType.Double),
                Array("cpus", s.Select(r => r.Metrics.AvgCpuTimeMicroseconds).ToArray(), NpgsqlDbType.Double),
                Array("logical_reads", s.Select(r => r.Metrics.AvgLogicalIoReads).ToArray(), NpgsqlDbType.Double),
                Array("physical_reads", s.Select(r => r.Metrics.AvgPhysicalIoReads).ToArray(), NpgsqlDbType.Double),
                Array("logical_writes", s.Select(r => r.Metrics.AvgLogicalIoWrites).ToArray(), NpgsqlDbType.Double),
                Array("row_counts", s.Select(r => r.Metrics.AvgRowCount).ToArray(), NpgsqlDbType.Double),
                Array("dops", s.Select(r => r.Metrics.AvgDop).ToArray(), NpgsqlDbType.Double),
                Array("memories", s.Select(r => r.Metrics.AvgMaxUsedMemoryPages).ToArray(), NpgsqlDbType.Double),
                Array("tempdbs", s.Select(r => r.Metrics.AvgTempDbSpacePages).ToArray(), NpgsqlDbType.Double),
                Array("log_bytes", s.Select(r => r.Metrics.AvgLogBytesUsed).ToArray(), NpgsqlDbType.Double),
            ],
            cancellationToken);
    }

    private async Task AddAndSaveAsync(IngestionRun run, CancellationToken cancellationToken)
    {
        _db.IngestionRuns.Add(run);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            // El resto de la ingesta escribe con SQL directo: nada debe quedar rastreado.
            _db.ChangeTracker.Clear();
        }
    }

    private static NpgsqlParameter Param(string name, object? value, NpgsqlDbType type) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private static NpgsqlParameter Array<T>(string name, T[] values, NpgsqlDbType elementType) =>
        new(name, NpgsqlDbType.Array | elementType) { Value = values };
}
