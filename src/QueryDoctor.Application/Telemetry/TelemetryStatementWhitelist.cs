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
    /// Consulta para inspeccionar el estado actual y configuración de Query Store (RF-CFG-04).
    /// </summary>
    public const string GetQueryStoreOptionsSql =
        "SELECT actual_state, actual_state_desc, readonly_reason, " +
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
    /// </summary>
    public const string GetQueryStoreViewColumnsSql =
        "SELECT c.name AS column_name FROM sys.columns c " +
        "JOIN sys.objects o ON c.object_id = o.object_id " +
        "WHERE o.name = @ViewName AND SCHEMA_NAME(o.schema_id) = 'sys' " +
        "ORDER BY c.column_id;";

    private static readonly FrozenDictionary<string, string> RegisteredStatements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [Keys.DetectEngineVersion] = DetectEngineVersionSql,
        [Keys.GetQueryStoreOptions] = GetQueryStoreOptionsSql,
        [Keys.CheckUserPermissions] = CheckUserPermissionsSql,
        [Keys.GetTopQueriesByTotalDuration] = GetTopQueriesByTotalDurationSql,
        [Keys.GetPlanXmlByPlanId] = GetPlanXmlByPlanIdSql,
        [Keys.GetQueryStoreViewColumns] = GetQueryStoreViewColumnsSql,
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
