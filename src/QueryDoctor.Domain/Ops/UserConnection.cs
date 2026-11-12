namespace QueryDoctor.Domain.Ops;

/// <summary>
/// Asignación de una conexión a un usuario con rol Desarrollador (RNF-SEG-12). Tabla ops.user_connection.
/// </summary>
public sealed class UserConnection
{
    public Guid UserId { get; set; }

    public Guid ConnectionId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }
}
