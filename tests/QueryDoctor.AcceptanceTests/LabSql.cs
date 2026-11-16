using System.Globalization;
using Microsoft.Data.SqlClient;
using QueryDoctor.LabTests;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// Lecturas que hace la PRUEBA (no la plataforma) contra el laboratorio, para comprobar desde fuera lo que la plataforma
/// no debe cambiar (CA-HU02.3). Solo SELECT, como qd_reader, con la excepción de TLS del laboratorio.
/// </summary>
internal static class LabSql
{
    public static async Task<IReadOnlyDictionary<string, string>> QueryStoreConfigurationAsync(string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = LabEnvironment.RequiredServer,
            InitialCatalog = database,
            UserID = LabEnvironment.Reader,
            Password = LabEnvironment.ReaderPassword(),
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true,
            ApplicationName = "QueryDoctor-AI AcceptanceTests",
        };

        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT desired_state_desc, actual_state_desc, readonly_reason, interval_length_minutes, " +
            "flush_interval_seconds, max_storage_size_mb, query_capture_mode_desc, size_based_cleanup_mode_desc, " +
            "stale_query_threshold_days, max_plans_per_query, wait_stats_capture_mode_desc " +
            "FROM sys.database_query_store_options;";

        await using var reader = await command.ExecuteReaderAsync();
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        if (await reader.ReadAsync())
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                snapshot[reader.GetName(i)] = reader.IsDBNull(i)
                    ? "NULL"
                    : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "NULL";
            }
        }

        return snapshot;
    }
}
