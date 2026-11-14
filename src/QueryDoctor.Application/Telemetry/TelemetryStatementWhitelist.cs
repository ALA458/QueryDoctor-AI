using System.Collections.Frozen;
using System.Security;
using System.Text.RegularExpressions;

namespace QueryDoctor.Application.Telemetry;

/// <summary>
/// Lista blanca inmutable de sentencias de telemetría permitidas (RNF-SEG-01, P1, CA-HU15.2).
/// Garantiza que ninguna sentencia arbitraria, DDL o DML pueda ser ejecutada en la base de datos objetivo.
/// </summary>
public static partial class TelemetryStatementWhitelist
{
    public static class Keys
    {
        public const string DetectEngineVersion = "DetectEngineVersion";
        public const string GetQueryStoreOptions = "GetQueryStoreOptions";
        public const string CheckUserPermissions = "CheckUserPermissions";
        public const string GetTopQueriesByTotalDuration = "GetTopQueriesByTotalDuration";
        public const string GetPlanXmlByPlanId = "GetPlanXmlByPlanId";
        public const string GetQueryStoreViewColumns = "GetQueryStoreViewColumns";
        public const string GetIngestionRuntimeStats = "GetIngestionRuntimeStats";
        public const string GetIngestionRuntimeStatsWithoutTempDbLog = "GetIngestionRuntimeStatsWithoutTempDbLog";
        public const string GetQueryTextById = "GetQueryTextById";
    }

    /// <summary>
    /// Consulta para detectar versión, edición y nivel de producto de la instancia SQL Server (RF-CFG-03).
    /// </summary>
    public const string DetectEngineVersionSql =
        "SELECT CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(128)) AS ProductVersion, " +
        "CAST(SERVERPROPERTY('ProductLevel') AS NVARCHAR(128)) AS ProductLevel, " +
        "CAST(SERVERPROPERTY('Edition') AS NVARCHAR(128)) AS Edition, " +
        "CAST(SERVERPROPERTY('EngineEdition') AS INT) AS EngineEdition;";

    /// <summary>
    /// Consulta para inspeccionar el estado actual y configuración de Query Store (RF-CFG-04, CA-HU02.1):
    /// estado actual y deseado, motivo de solo lectura, intervalo de agregación, modo de captura y retención
    /// (stale_query_threshold_days). Todas las columnas existen desde SQL Server 2016 y en Azure SQL Database.
    /// </summary>
    public const string GetQueryStoreOptionsSql =
        "SELECT actual_state, actual_state_desc, desired_state_desc, readonly_reason, " +
        "interval_length_minutes, stale_query_threshold_days, " +
        "current_storage_size_mb, max_storage_size_mb, query_capture_mode_desc, size_based_cleanup_mode_desc " +
        "FROM sys.database_query_store_options;";

    /// <summary>
    /// Consulta para validar permisos de solo lectura del usuario actual sobre la base de datos (RF-CFG-02, CA-HU15.1).
    /// </summary>
    public const string CheckUserPermissionsSql =
        "SELECT permission_name FROM fn_my_permissions(NULL, 'DATABASE');";

    /// <summary>
    /// Consulta de ingesta para obtener las consultas con mayor consumo acumulado de duración (RF-ING-01, RF-ING-02).
    /// </summary>
    public const string GetTopQueriesByTotalDurationSql =
        "SELECT TOP (@TopCount) q.query_id, q.query_text_id, qt.query_sql_text, " +
        "SUM(rs.count_executions) AS total_executions, " +
        "SUM(rs.avg_duration * rs.count_executions) AS total_duration_us, " +
        "SUM(rs.avg_cpu_time * rs.count_executions) AS total_cpu_time_us, " +
        "SUM(rs.avg_logical_io_reads * rs.count_executions) AS total_logical_reads " +
        "FROM sys.query_store_query q " +
        "JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id " +
        "JOIN sys.query_store_plan p ON q.query_id = p.query_id " +
        "JOIN sys.query_store_runtime_stats rs ON p.plan_id = rs.plan_id " +
        "WHERE rs.last_execution_time >= @MinExecutionTime " +
        "GROUP BY q.query_id, q.query_text_id, qt.query_sql_text " +
        "ORDER BY total_duration_us DESC;";

    /// <summary>
    /// Consulta para obtener el Showplan XML de un plan específico (RF-ING-04).
    /// </summary>
    public const string GetPlanXmlByPlanIdSql =
        "SELECT p.plan_id, p.query_id, p.query_plan FROM sys.query_store_plan p WHERE p.plan_id = @PlanId;";

    /// <summary>
    /// Consulta para inspeccionar las columnas disponibles de las vistas de Query Store (RF-CFG-03, RF-CFG-04, RNF-COM-04).
    /// Permite a CapabilityProbe generar el perfil de capacidades dinámico por versión.
    /// Usa sys.all_columns/sys.all_objects: las vistas del esquema sys no aparecen en sys.columns/sys.objects
    /// (hallazgo de S1-07, lab/results/S1-07-verificacion.md §4).
    /// </summary>
    public const string GetQueryStoreViewColumnsSql =
        "SELECT c.name AS column_name FROM sys.all_columns c " +
        "JOIN sys.all_objects o ON c.object_id = o.object_id " +
        "WHERE o.name = @ViewName AND SCHEMA_NAME(o.schema_id) = 'sys' " +
        "ORDER BY c.column_id;";

    // S1-11: partes comunes de las dos variantes de la sentencia de ingesta. Solo cambian las columnas de TempDB y
    // bytes de log (RF-ING-03), que no existen en SQL Server 2016: así RNF-COM-04 se cumple sin SQL dinámico.
    private const string IngestionRuntimeStatsSelect =
        "SELECT rs.plan_id, p.query_id, q.query_text_id, q.query_hash, p.query_plan_hash, p.engine_version, " +
        "OBJECT_SCHEMA_NAME(q.object_id) AS object_schema, OBJECT_NAME(q.object_id) AS object_name, " +
        "rs.execution_type, rs.runtime_stats_interval_id, i.start_time, i.end_time, " +
        "CASE WHEN i.end_time <= SYSDATETIMEOFFSET() THEN 1 ELSE 0 END AS is_closed, " +
        "rs.count_executions, rs.avg_duration, rs.avg_cpu_time, rs.avg_logical_io_reads, rs.avg_physical_io_reads, " +
        "rs.avg_logical_io_writes, rs.avg_rowcount, rs.avg_dop, rs.avg_query_max_used_memory, ";

    // Top-N (RF-ING-10, D1 de S1-11): las N consultas con mayor duración total (Σ avg_duration × count_executions)
    // en los intervalos posteriores a la marca de agua, con desempate por query_id para que sea determinista.
    // Subconsulta y no WITH: así statement_log solo muestra sentencias que empiezan por SELECT (CA-HU15.2).
    private const string IngestionRuntimeStatsFrom =
        "FROM sys.query_store_runtime_stats rs " +
        "JOIN sys.query_store_runtime_stats_interval i ON i.runtime_stats_interval_id = rs.runtime_stats_interval_id " +
        "JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id " +
        "JOIN sys.query_store_query q ON q.query_id = p.query_id " +
        "JOIN (SELECT TOP (@TopCount) p2.query_id " +
        "FROM sys.query_store_runtime_stats rs2 " +
        "JOIN sys.query_store_runtime_stats_interval i2 ON i2.runtime_stats_interval_id = rs2.runtime_stats_interval_id " +
        "JOIN sys.query_store_plan p2 ON p2.plan_id = rs2.plan_id " +
        "JOIN sys.query_store_query q2 ON q2.query_id = p2.query_id " +
        "WHERE rs2.runtime_stats_interval_id > @AfterIntervalId AND i2.start_time >= @MinStartTime " +
        "AND q2.is_internal_query = 0 " +
        "GROUP BY p2.query_id " +
        "ORDER BY SUM(rs2.avg_duration * rs2.count_executions) DESC, p2.query_id) t ON t.query_id = p.query_id " +
        "WHERE rs.runtime_stats_interval_id > @AfterIntervalId AND i.start_time >= @MinStartTime " +
        "ORDER BY rs.runtime_stats_interval_id, rs.plan_id, rs.execution_type;";

    /// <summary>
    /// Ingesta incremental (S1-11; RF-ING-01, 02, 03, 05, 10): filas crudas de sys.query_store_runtime_stats de las
    /// N consultas de mayor consumo, en los intervalos posteriores a la marca de agua (cerrados y activos), con los
    /// identificadores de consulta, texto y plan. La agregación ponderada (RF-ING-04) se hace después, en .NET.
    /// SQL Server 2017+ y Azure SQL.
    /// </summary>
    public const string GetIngestionRuntimeStatsSql =
        IngestionRuntimeStatsSelect +
        "rs.avg_tempdb_space_used, rs.avg_log_bytes_used " +
        IngestionRuntimeStatsFrom;

    /// <summary>
    /// Igual que <see cref="GetIngestionRuntimeStatsSql"/> para motores sin las columnas de TempDB y bytes de log
    /// (SQL Server 2016): se devuelven nulas y la ingesta lo deja registrado (RNF-COM-04).
    /// </summary>
    public const string GetIngestionRuntimeStatsWithoutTempDbLogSql =
        IngestionRuntimeStatsSelect +
        "CAST(NULL AS float) AS avg_tempdb_space_used, CAST(NULL AS float) AS avg_log_bytes_used " +
        IngestionRuntimeStatsFrom;

    /// <summary>Texto SQL de una consulta de Query Store, solo para textos que aún no se guardaron (S1-11, RF-ING-01).</summary>
    public const string GetQueryTextByIdSql =
        "SELECT qt.query_text_id, qt.query_sql_text FROM sys.query_store_query_text qt WHERE qt.query_text_id = @QueryTextId;";

    private static readonly FrozenDictionary<string, string> RegisteredStatements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [Keys.DetectEngineVersion] = DetectEngineVersionSql,
        [Keys.GetQueryStoreOptions] = GetQueryStoreOptionsSql,
        [Keys.CheckUserPermissions] = CheckUserPermissionsSql,
        [Keys.GetTopQueriesByTotalDuration] = GetTopQueriesByTotalDurationSql,
        [Keys.GetPlanXmlByPlanId] = GetPlanXmlByPlanIdSql,
        [Keys.GetQueryStoreViewColumns] = GetQueryStoreViewColumnsSql,
        [Keys.GetIngestionRuntimeStats] = GetIngestionRuntimeStatsSql,
        [Keys.GetIngestionRuntimeStatsWithoutTempDbLog] = GetIngestionRuntimeStatsWithoutTempDbLogSql,
        [Keys.GetQueryTextById] = GetQueryTextByIdSql,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] ForbiddenTokens =
    [
        "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "EXEC",
        "EXECUTE", "TRUNCATE", "MERGE", "GRANT", "REVOKE", "DENY",
        "SHUTDOWN", "BACKUP", "RESTORE", "DBCC", "XP_", "SP_EXECUTESQL"
    ];

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    /// Determina si una clave de sentencia se encuentra registrada en la lista blanca.
    /// </summary>
    public static bool ContainsKey(string statementKey) =>
        !string.IsNullOrWhiteSpace(statementKey) && RegisteredStatements.ContainsKey(statementKey);

    /// <summary>
    /// Obtiene el texto SQL constante asociado a una clave registrada.
    /// </summary>
    /// <param name="statementKey">Clave de la consulta.</param>
    /// <returns>Texto SQL preaprobado.</returns>
    /// <exception cref="SecurityException">Si la clave no está en la lista blanca.</exception>
    public static string GetSql(string statementKey)
    {
        if (string.IsNullOrWhiteSpace(statementKey) || !RegisteredStatements.TryGetValue(statementKey, out var sql))
        {
            throw new SecurityException($"Ejecución rechazada: La clave de sentencia '{statementKey}' no forma parte de la lista blanca autorizada (RNF-SEG-01).");
        }

        return sql;
    }

    /// <summary>
    /// Valida que la sentencia sea de solo lectura estricta (SELECT) y cumpla con los invariantes de seguridad (CA-HU15.2, RNF-SEG-01).
    /// </summary>
    /// <param name="sqlText">Texto SQL a verificar.</param>
    /// <exception cref="SecurityException">Si el texto contiene operaciones de modificación o no inicia con SELECT.</exception>
    public static void EnsureReadOnlySelectStatement(string sqlText)
    {
        if (string.IsNullOrWhiteSpace(sqlText))
        {
            throw new SecurityException("La sentencia SQL no puede ser nula ni vacía.");
        }

        var trimmed = sqlText.TrimStart();

        // Debe iniciar estrictamente con SELECT o con WITH (para Common Table Expressions que terminan en SELECT)
        var startsWithSelect = trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase);
        var startsWithWith = trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

        if (!startsWithSelect && !startsWithWith)
        {
            throw new SecurityException("Invariante RNF-SEG-01 violado: Únicamente sentencias de tipo SELECT están permitidas.");
        }

        // Inspeccionar palabras clave prohibidas como tokens aislados
        foreach (var token in ForbiddenTokens)
        {
            var pattern = $@"\b{Regex.Escape(token)}\b";
            if (Regex.IsMatch(sqlText, pattern, RegexOptions.IgnoreCase))
            {
                throw new SecurityException($"Invariante RNF-SEG-01 violado: La palabra clave prohibida '{token}' fue detectada en la sentencia.");
            }
        }
    }

    /// <summary>
    /// Valida una clave y su texto SQL correspondiente contra la lista blanca.
    /// </summary>
    public static void ValidateStatementOrThrow(string statementKey, string sqlText)
    {
        var whitelistedSql = GetSql(statementKey);

        EnsureReadOnlySelectStatement(sqlText);

        var normalizedInput = NormalizeSql(sqlText);
        var normalizedWhitelisted = NormalizeSql(whitelistedSql);

        if (!string.Equals(normalizedInput, normalizedWhitelisted, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Invariante RNF-SEG-01 violado: El texto SQL ejecutado no coincide exactamente con la sentencia registrada para la clave '{statementKey}'.");
        }
    }

    private static string NormalizeSql(string sql) =>
        WhitespaceRegex().Replace(sql.Trim(), " ");
}
