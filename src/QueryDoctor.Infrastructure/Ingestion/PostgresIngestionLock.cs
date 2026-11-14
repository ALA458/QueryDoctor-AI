using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using QueryDoctor.Application.Ingestion;

namespace QueryDoctor.Infrastructure.Ingestion;

/// <summary>
/// Bloqueo por conexión con pg_try_advisory_lock (arquitectura §6.2, S1-11). Funciona con varias instancias del
/// Worker porque el candado vive en PostgreSQL. Es de sesión y se toma sobre una conexión propia, sin pool, que se
/// mantiene abierta toda la ingesta: si el proceso muere, PostgreSQL cierra la sesión y libera el candado solo.
/// </summary>
/// <remarks>
/// Por qué no [DisableConcurrentExecution] de Hangfire: bloquea por método (serializaría las ingestas de TODAS las
/// conexiones), espera en lugar de omitir, y su candado de tabla puede quedar tomado hasta su tiempo límite si el
/// Worker muere. Además este bloqueo también cubre una ingesta lanzada fuera de Hangfire.
/// </remarks>
public sealed class PostgresIngestionLock : IIngestionLock
{
    private const string KeyNamespace = "querydoctor:ingest:";

    private readonly string _connectionString;

    public PostgresIngestionLock(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Sin pool: el candado es de sesión y no debe sobrevivir en una conexión reciclada.
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
    }

    /// <summary>
    /// Clave de 64 bits: primeros 8 bytes de SHA-256("querydoctor:ingest:" + id). Determinista y con espacio propio.
    /// Si dos conexiones chocaran (probabilidad despreciable), una se omitiría como AlreadyRunning: falla segura.
    /// </summary>
    public static long KeyFor(Guid connectionId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(KeyNamespace + connectionId.ToString("D")));
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }

    public async Task<IAsyncDisposable?> TryAcquireAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var key = KeyFor(connectionId);
        var connection = new NpgsqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", key);

            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new Lease(connection, key);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Lease(NpgsqlConnection connection, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            catch (NpgsqlException)
            {
                // Si la sesión ya se perdió, PostgreSQL liberó el candado al cerrarla: no hay nada que deshacer.
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
