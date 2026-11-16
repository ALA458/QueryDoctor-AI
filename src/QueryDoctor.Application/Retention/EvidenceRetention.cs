using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;

namespace QueryDoctor.Application.Retention;

/// <summary>
/// Plazo de retención del texto SQL y de los planes ingeridos (S2-13, RNF-SEG-10). Se fija por configuración o variable de
/// entorno (<c>Retention__SqlTextAndPlanDays</c>), nunca desde la interfaz ("umbrales desde la UI" está fuera del MVP).
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    public const int MinDays = 1;

    public const int MaxDays = 3650;

    /// <summary>Días desde la ingesta tras los cuales se purgan el texto y el plan. 30, como Query Store por defecto.</summary>
    public int SqlTextAndPlanDays { get; set; } = 30;

    /// <summary>Un valor fuera de rango impide arrancar el Worker: purgar con un plazo equivocado no tiene vuelta atrás.</summary>
    public void Validate()
    {
        if (SqlTextAndPlanDays is < MinDays or > MaxDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SqlTextAndPlanDays),
                $"Retention:SqlTextAndPlanDays debe estar entre {MinDays} y {MaxDays} (RNF-SEG-10).");
        }
    }
}

/// <summary>Filas purgadas de cada tabla. Solo conteos: es lo único que llega al log (RNF-SEG-09).</summary>
public sealed record EvidencePurgeCounts(int SqlTexts, int Plans, int Findings)
{
    public static readonly EvidencePurgeCounts None = new(0, 0, 0);

    public int Total => SqlTexts + Plans + Findings;
}

/// <summary>Qué alcanza una purga: una conexión entera o una sola consulta.</summary>
public enum EvidenceScopeKind
{
    Connection,
    Query,
}

public sealed record EvidenceScope(EvidenceScopeKind Kind, Guid Id)
{
    public static EvidenceScope ForConnection(Guid connectionId) => new(EvidenceScopeKind.Connection, connectionId);

    public static EvidenceScope ForQuery(Guid queryId) => new(EvidenceScopeKind.Query, queryId);

    public string SubjectType => Kind == EvidenceScopeKind.Connection
        ? AuditEventDraft.SubjectConnection
        : AuditEventDraft.SubjectQuery;
}

/// <summary>
/// Purga de evidencia (S2-13). Vacía el texto SQL, el XML del plan y la evidencia de los hallazgos de esos planes y deja las
/// filas, ids, hashes, métricas, puntajes y explicaciones. Nunca toca <c>audit.*</c>: por eso la traza sigue verificando.
/// </summary>
public interface IEvidencePurgeStore
{
    /// <summary>Si el objeto existe para quien pregunta (la Api pasa por el filtro por conexión de S2-12).</summary>
    Task<bool> ExistsAsync(EvidenceScope scope, CancellationToken cancellationToken);

    /// <summary>Conexiones con algún texto o plan ingerido antes de <paramref name="ingestedBefore"/> y aún sin purgar.</summary>
    Task<IReadOnlyList<Guid>> ConnectionsWithExpiredEvidenceAsync(DateTimeOffset ingestedBefore, CancellationToken cancellationToken);

    /// <summary>
    /// Purga en UNA transacción junto con su evento de auditoría (RF-TRZ-05): si la auditoría falla, no se borra nada.
    /// Con <paramref name="ingestedBefore"/> nulo purga todo el alcance (borrado a pedido). El evento lo arma
    /// <paramref name="audit"/> con los conteos; si devuelve null, no se escribe ninguno.
    /// </summary>
    Task<EvidencePurgeCounts> PurgeAsync(
        EvidenceScope scope,
        DateTimeOffset? ingestedBefore,
        DateTimeOffset now,
        Func<EvidencePurgeCounts, AuditEventDraft?> audit,
        CancellationToken cancellationToken);
}

/// <summary>Resultado de la purga automática de una corrida, para el log del trabajo.</summary>
public sealed record RetentionRunOutcome(DateTimeOffset IngestedBefore, IReadOnlyList<(Guid ConnectionId, EvidencePurgeCounts Counts)> Purged);

/// <summary>
/// Purga automática al vencer el plazo (S2-13, RNF-SEG-10). La corre el trabajo <c>retention</c> del Worker con acceso
/// Sistema. Cada conexión es una transacción con su evento <see cref="AuditEventType.DataDeleted"/> (D8), sin usuario.
/// </summary>
public sealed class PurgeExpiredEvidence
{
    /// <summary>Nombre con el que la purga automática aparece en la auditoría (no es un usuario de Identity).</summary>
    public const string SystemActor = "system:retention";

    private readonly IEvidencePurgeStore _store;
    private readonly RetentionOptions _options;
    private readonly TimeProvider _clock;

    public PurgeExpiredEvidence(IEvidencePurgeStore store, RetentionOptions options, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<RetentionRunOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();
        var now = _clock.GetUtcNow();
        var ingestedBefore = now.AddDays(-_options.SqlTextAndPlanDays);

        var purged = new List<(Guid, EvidencePurgeCounts)>();
        foreach (var connectionId in await _store.ConnectionsWithExpiredEvidenceAsync(ingestedBefore, cancellationToken))
        {
            var counts = await _store.PurgeAsync(
                EvidenceScope.ForConnection(connectionId),
                ingestedBefore,
                now,
                c => c.Total == 0
                    ? null
                    : new AuditEventDraft(
                        AuditEventType.DataDeleted,
                        UserId: null,
                        UserName: SystemActor,
                        Outcome: AuditEventDraft.OutcomeOk,
                        SubjectType: AuditEventDraft.SubjectConnection,
                        SubjectId: connectionId),
                cancellationToken);

            if (counts.Total > 0)
            {
                purged.Add((connectionId, counts));
            }
        }

        return new RetentionRunOutcome(ingestedBefore, purged);
    }
}

/// <summary>Quién pide el borrado: el DBA autenticado.</summary>
public sealed record EvidenceDeletionActor(Guid? UserId, string? UserName);

public abstract record DeleteEvidenceResult
{
    private DeleteEvidenceResult()
    {
    }

    public sealed record Deleted(EvidencePurgeCounts Counts) : DeleteEvidenceResult;

    public sealed record NotFound : DeleteEvidenceResult;
}

/// <summary>
/// Borrado a pedido del DBA (S2-13, RNF-SEG-10): texto SQL, planes y evidencia de una consulta o de toda una conexión, sin
/// esperar al plazo. Siempre deja su evento <see cref="AuditEventType.DataDeleted"/> con el usuario, en la misma
/// transacción; si no quedaba nada por borrar, el evento dice <see cref="AuditEventDraft.OutcomeNothingToDelete"/>.
/// No borra conexiones ni usuarios.
/// </summary>
public sealed class DeleteEvidence
{
    private readonly IEvidencePurgeStore _store;
    private readonly TimeProvider _clock;

    public DeleteEvidence(IEvidencePurgeStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<DeleteEvidenceResult> ExecuteAsync(
        EvidenceScope scope,
        EvidenceDeletionActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(actor);

        if (!await _store.ExistsAsync(scope, cancellationToken))
        {
            return new DeleteEvidenceResult.NotFound();
        }

        var counts = await _store.PurgeAsync(
            scope,
            ingestedBefore: null,
            _clock.GetUtcNow(),
            c => new AuditEventDraft(
                AuditEventType.DataDeleted,
                actor.UserId,
                actor.UserName,
                c.Total == 0 ? AuditEventDraft.OutcomeNothingToDelete : AuditEventDraft.OutcomeOk,
                scope.SubjectType,
                scope.Id),
            cancellationToken);

        return new DeleteEvidenceResult.Deleted(counts);
    }
}
