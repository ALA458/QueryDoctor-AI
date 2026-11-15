namespace QueryDoctor.Application.Connections;

/// <summary>Un Desarrollador asignado a una conexión (GET /connections/{id}/assignments).</summary>
public sealed record AssignmentView(Guid UserId, string UserName, DateTimeOffset AssignedAt);

/// <summary>Usuario con rol Desarrollador, para que el DBA elija a quién asignar (GET /users?role=Desarrollador).</summary>
public sealed record DeveloperView(Guid Id, string UserName);

/// <summary>Qué es un usuario para efectos de una asignación.</summary>
public enum AssignmentCandidate
{
    NotFound,
    NotDeveloper,
    Developer,
}

/// <summary>Asignaciones en <c>ops.user_connection</c> (S2-12, RNF-SEG-12). Solo las usa el DBA desde la Api.</summary>
public interface IConnectionAssignmentStore
{
    /// <summary>Pasa por el filtro por conexión: el DBA ve todas.</summary>
    Task<bool> ConnectionExistsAsync(Guid connectionId, CancellationToken cancellationToken);

    Task<AssignmentCandidate> GetCandidateAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignmentView>> ListAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>Idempotente: <see langword="true"/> si la creó, <see langword="false"/> si ya existía.</summary>
    Task<bool> AssignAsync(Guid connectionId, Guid userId, DateTimeOffset assignedAt, CancellationToken cancellationToken);

    /// <summary><see langword="true"/> si existía y se quitó.</summary>
    Task<bool> UnassignAsync(Guid connectionId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeveloperView>> ListDevelopersAsync(CancellationToken cancellationToken);
}

/// <summary>Resultado de asignar una conexión.</summary>
public abstract record AssignConnectionResult
{
    private AssignConnectionResult()
    {
    }

    /// <summary>Asignada. <paramref name="Created"/> es falso si ya lo estaba (no se audita dos veces).</summary>
    public sealed record Assigned(bool Created) : AssignConnectionResult;

    public sealed record ConnectionNotFound : AssignConnectionResult;

    public sealed record UserNotFound : AssignConnectionResult;

    /// <summary>Solo se asigna a usuarios con rol Desarrollador: el DBA ya ve todo.</summary>
    public sealed record NotDeveloper : AssignConnectionResult;
}

/// <summary>
/// Gestión de asignaciones por el DBA (S2-12, RNF-SEG-12). Asignar o quitar surte efecto en la siguiente solicitud del
/// Desarrollador, porque su acceso se lee de la base en cada una.
/// </summary>
public sealed class ManageConnectionAssignments
{
    private readonly IConnectionAssignmentStore _store;
    private readonly TimeProvider _clock;

    public ManageConnectionAssignments(IConnectionAssignmentStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary><see langword="null"/> si la conexión no existe.</summary>
    public async Task<IReadOnlyList<AssignmentView>?> ListAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
        await _store.ConnectionExistsAsync(connectionId, cancellationToken)
            ? await _store.ListAsync(connectionId, cancellationToken)
            : null;

    public async Task<AssignConnectionResult> AssignAsync(Guid connectionId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _store.ConnectionExistsAsync(connectionId, cancellationToken))
        {
            return new AssignConnectionResult.ConnectionNotFound();
        }

        return await _store.GetCandidateAsync(userId, cancellationToken) switch
        {
            AssignmentCandidate.NotFound => new AssignConnectionResult.UserNotFound(),
            AssignmentCandidate.NotDeveloper => new AssignConnectionResult.NotDeveloper(),
            _ => new AssignConnectionResult.Assigned(
                await _store.AssignAsync(connectionId, userId, _clock.GetUtcNow(), cancellationToken)),
        };
    }

    /// <summary><see langword="false"/> si la conexión no existe o no estaba asignada a ese usuario.</summary>
    public async Task<bool> UnassignAsync(Guid connectionId, Guid userId, CancellationToken cancellationToken = default) =>
        await _store.ConnectionExistsAsync(connectionId, cancellationToken)
        && await _store.UnassignAsync(connectionId, userId, cancellationToken);

    public Task<IReadOnlyList<DeveloperView>> ListDevelopersAsync(CancellationToken cancellationToken = default) =>
        _store.ListDevelopersAsync(cancellationToken);
}
