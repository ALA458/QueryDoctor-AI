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

    /// <summary>Perfil de capacidades vigente (uno por conexión), o null si nunca hubo sondeo correcto.</summary>
    Task<CapabilityProfile?> GetProfileAsync(Guid connectionId, CancellationToken cancellationToken);

    void AddProfile(CapabilityProfile profile);

    /// <summary>
    /// Marca el sondeo como fallido solo si la conexión sigue en Pending, con un único UPDATE condicional e
    /// independiente del rastreador de cambios (auditoría M1: el sondeo se cayó por persistencia y la conexión
    /// no puede quedar en Pending para siempre, RF-CFG-02).
    /// </summary>
    /// <returns>true si la marcó; false si ya no estaba en Pending (o no existe).</returns>
    Task<bool> MarkProbeFailedIfPendingAsync(Guid id, ProbeFailureCause cause, DateTimeOffset now, CancellationToken cancellationToken);

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
