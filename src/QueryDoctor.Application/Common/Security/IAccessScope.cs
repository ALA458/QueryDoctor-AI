namespace QueryDoctor.Application.Common.Security;

/// <summary>Quién lee los datos de las conexiones (S2-12, RNF-SEG-12).</summary>
public enum AccessKind
{
    /// <summary>Sin usuario, rol desconocido o scope sin inicializar: cero filas (denegación por defecto).</summary>
    None = 0,

    /// <summary>Rol Desarrollador: solo las conexiones de <c>ops.user_connection</c>.</summary>
    Developer,

    /// <summary>Rol DBA: todas las conexiones.</summary>
    Dba,

    /// <summary>Procesos de la plataforma (Worker, Hangfire, migraciones, siembra): acceso completo declarado.</summary>
    System,
}

/// <summary>
/// Contexto de acceso de la unidad de trabajo actual (S2-12, RNF-SEG-12). El filtro global de EF Core lo lee en cada
/// consulta, así que el filtrado por conexión asignada vive en la capa de datos y no en cada endpoint.
/// </summary>
public interface IAccessScope
{
    AccessKind Kind { get; }

    /// <summary>Conexiones asignadas; solo tiene sentido con <see cref="AccessKind.Developer"/>.</summary>
    IReadOnlyCollection<Guid> AssignedConnectionIds { get; }
}

/// <summary>
/// Acceso completo de los procesos de la plataforma. Se registra de forma explícita en el host del Worker y se pasa
/// a mano en las migraciones y en las pruebas; nunca se deduce de que "no hay usuario".
/// </summary>
public sealed class SystemAccessScope : IAccessScope
{
    public static readonly SystemAccessScope Instance = new();

    private SystemAccessScope()
    {
    }

    public AccessKind Kind => AccessKind.System;

    public IReadOnlyCollection<Guid> AssignedConnectionIds => [];
}

/// <summary>Lee las conexiones asignadas a un usuario (ops.user_connection), sin caché: quitar una surte efecto de inmediato.</summary>
public interface IAssignedConnectionsReader
{
    Task<Guid[]> ForUserAsync(Guid userId, CancellationToken cancellationToken);
}
