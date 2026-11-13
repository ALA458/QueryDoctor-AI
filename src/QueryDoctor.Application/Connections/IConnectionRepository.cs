using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Connections;

/// <summary>
/// Persistencia de conexiones para los casos de uso (S1-09). La implementación con EF Core vive en
/// Infrastructure; Application no conoce EF Core (regla de NetArchTest).
/// </summary>
public interface IConnectionRepository
{
    /// <summary>
    /// Busca por la clave natural: <paramref name="normalizedServer"/> exacto y la base sin distinguir
    /// mayúsculas (misma regla que la UK (server, lower(database_name))).
    /// </summary>
    Task<Connection?> FindByAddressAsync(string normalizedServer, string databaseName, CancellationToken cancellationToken);

    Task<Connection?> GetAsync(Guid id, CancellationToken cancellationToken);

    void Add(Connection connection);

    /// <summary>Confirma los cambios pendientes.</summary>
    /// <exception cref="DuplicateConnectionException">Otra solicitud registró la misma base al mismo tiempo.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Encola trabajos de sondeo. El argumento es solo el identificador, nunca SQL ni secretos (§8.1).</summary>
public interface IProbeJobScheduler
{
    void EnqueueProbe(Guid connectionId);
}

/// <summary>La clave natural (servidor, base) ya existe: la UK de core.connection rechazó el registro.</summary>
public sealed class DuplicateConnectionException : Exception
{
    public DuplicateConnectionException()
    {
    }

    public DuplicateConnectionException(string message)
        : base(message)
    {
    }

    public DuplicateConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
