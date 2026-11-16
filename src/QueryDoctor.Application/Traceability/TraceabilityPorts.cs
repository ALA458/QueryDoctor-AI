using QueryDoctor.Application.Explanations;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Audit;

namespace QueryDoctor.Application.Traceability;

/// <summary>Evento a registrar en el log de auditoría (RF-TRZ-05). Sin contraseñas ni SQL (RNF-SEG-09).</summary>
public sealed record AuditEventDraft(
    AuditEventType EventType,
    Guid? UserId,
    string? UserName,
    string Outcome,
    string? SubjectType = null,
    Guid? SubjectId = null,
    Guid? RelatedUserId = null)
{
    public const string OutcomeOk = "ok";
    public const string OutcomeRejected = "rejected";
    public const string OutcomeLocked = "locked";
    public const string OutcomeUnknownUser = "unknown_user";
    public const string SubjectConnection = "connection";

    /// <summary>S2-13: el borrado a pedido de una sola consulta.</summary>
    public const string SubjectQuery = "query";

    /// <summary>S2-13: el borrado a pedido no encontró nada por borrar (ya estaba purgado). Se audita igual.</summary>
    public const string OutcomeNothingToDelete = "nothing_to_delete";
    public const string SubjectUser = "user";
}

/// <summary>
/// Log de auditoría de solo adición y encadenado (S2-06, RF-TRZ-05). Cada llamada es una transacción propia con
/// inserción serializada.
/// </summary>
public interface IAuditLog
{
    Task AppendAsync(AuditEventDraft draft, CancellationToken cancellationToken);
}

/// <summary>Filtro del registro de trazabilidad (RF-TRZ-03, CA-HU14.3).</summary>
public sealed record TraceFilter(Guid? QueryId, Guid? ExplanationId, DateTimeOffset? From, DateTimeOffset? To, int Take);

/// <summary>Filtro del log de auditoría.</summary>
public sealed record AuditFilter(AuditEventType? EventType, DateTimeOffset? From, DateTimeOffset? To, int Take);

/// <summary>Consultas de solo lectura sobre la traza y la auditoría, más la verificación de sus cadenas.</summary>
public interface ITraceabilityQueries
{
    Task<IReadOnlyList<TraceEntry>> SearchTraceAsync(TraceFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEvent>> SearchAuditAsync(AuditFilter filter, CancellationToken cancellationToken);

    Task<ChainVerification> VerifyTraceAsync(CancellationToken cancellationToken);

    Task<ChainVerification> VerifyAuditAsync(CancellationToken cancellationToken);
}

/// <summary>Arma la entrada de traza de RF-TRZ-01 a partir de una explicación en estado final.</summary>
public static class TraceEntries
{
    /// <summary>
    /// Copia de la explicación lo que pide RF-TRZ-01 (hallazgos, catálogo, modelo, prompt, hash, validación). La
    /// secuencia, la fecha y los hashes los completa el escritor bajo el bloqueo de la cadena.
    /// </summary>
    public static TraceEntry FromExplanation(Explanation explanation)
    {
        ArgumentNullException.ThrowIfNull(explanation);
        if (explanation.State is ExplanationState.Pending or ExplanationState.Running)
        {
            throw new InvalidOperationException("Solo una explicación en estado final entra en la traza.");
        }

        return new TraceEntry
        {
            ExplanationId = explanation.Id,
            QueryId = explanation.QueryId,
            ConnectionId = explanation.ConnectionId,
            State = explanation.State.ToContractCode(),
            Cause = explanation.Cause?.ToString(),
            ValidationStatus = explanation.ValidationStatus,
            FindingCodesJson = explanation.FindingCodesJson,
            CatalogVersion = explanation.CatalogVersion,
            Model = explanation.Model,
            PromptVersion = explanation.PromptVersion,
            LlmProfile = explanation.LlmProfile,
            ServedBy = explanation.ServedBy,
            DataPolicy = explanation.DataPolicy,
            ContextHash = explanation.ContextHash,
            ReusedFromId = explanation.ReusedFromId,
        };
    }
}
