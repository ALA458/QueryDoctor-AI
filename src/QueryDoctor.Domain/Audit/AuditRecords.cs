namespace QueryDoctor.Domain.Audit;

/// <summary>
/// Entrada del registro de trazabilidad (tabla <c>audit.trace_entry</c>, S2-06, RF-TRZ-01). Una por cada explicación
/// que llega a un estado final. Solo identificadores, códigos, versiones y fechas: nunca SQL ni texto del modelo, así
/// que la purga de datos (S2-13) no la afecta. Solo adición y encadenada por hash (RF-TRZ-02).
/// </summary>
public sealed class TraceEntry
{
    /// <summary>Posición en la cadena (identidad).</summary>
    public long Seq { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid ExplanationId { get; set; }

    public Guid QueryId { get; set; }

    public Guid ConnectionId { get; set; }

    /// <summary>Estado final de la explicación (<c>COMPLETADA</c>, <c>IA_NO_DISPONIBLE</c>, <c>NO_GENERADA</c>).</summary>
    public string State { get; set; } = string.Empty;

    public string? Cause { get; set; }

    /// <summary>Resultado de la validación: <c>TRAZADA</c> o <c>NO_VERIFICADA</c> (RF-IA-10).</summary>
    public string? ValidationStatus { get; set; }

    /// <summary>Códigos de los hallazgos usados, en JSON.</summary>
    public string FindingCodesJson { get; set; } = "[]";

    public string? CatalogVersion { get; set; }

    public string? Model { get; set; }

    public string? PromptVersion { get; set; }

    public string? LlmProfile { get; set; }

    public string? ServedBy { get; set; }

    public string? DataPolicy { get; set; }

    public string? ContextHash { get; set; }

    /// <summary>Explicación original cuando el resultado vino de la caché (RF-IA-07).</summary>
    public Guid? ReusedFromId { get; set; }

    /// <summary>Hash de la entrada anterior de la cadena (64 ceros en la primera).</summary>
    public string PrevHash { get; set; } = string.Empty;

    /// <summary>SHA-256 de <see cref="PrevHash"/> más el contenido canónico de esta entrada.</summary>
    public string EntryHash { get; set; } = string.Empty;
}

/// <summary>Tipo de evento del log de auditoría (RF-TRZ-05, CA-HU14.4).</summary>
public enum AuditEventType
{
    LoginSucceeded = 1,
    LoginFailed = 2,
    LoginLockedOut = 3,
    Logout = 4,
    PasswordChanged = 5,
    ConnectionRegistered = 6,
    ConnectionReRegistered = 7,
    ManualIngestionRequested = 8,

    /// <summary>Sin emisor todavía: los umbrales desde la interfaz están fuera del MVP.</summary>
    ThresholdsChanged = 9,

    /// <summary>Sin emisor todavía: el borrado de datos llega con S2-13.</summary>
    DataDeleted = 10,

    /// <summary>Un DBA asignó una conexión a un Desarrollador (S2-12, RNF-SEG-12).</summary>
    ConnectionAssigned = 11,

    /// <summary>Un DBA quitó la asignación de una conexión a un Desarrollador (S2-12).</summary>
    ConnectionUnassigned = 12,
}

/// <summary>
/// Evento del log de auditoría (tabla <c>audit.audit_event</c>, S2-06, RF-TRZ-05). Usuario, marca de tiempo, tipo,
/// objeto afectado y resultado. Nunca contraseñas, credenciales ni SQL (RNF-SEG-09). Solo adición y encadenado.
/// </summary>
public sealed class AuditEvent
{
    public long Seq { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public AuditEventType EventType { get; set; }

    /// <summary>Usuario autenticado, o <see langword="null"/> si no lo hay (login fallido de un usuario inexistente).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Nombre de usuario tal como se escribió o el de la sesión. Nunca la contraseña.</summary>
    public string? UserName { get; set; }

    /// <summary>Tipo del objeto afectado (por ejemplo <c>connection</c>).</summary>
    public string? SubjectType { get; set; }

    public Guid? SubjectId { get; set; }

    /// <summary>
    /// Segundo objeto del evento, cuando lo hay: el Desarrollador al que se asigna o quita una conexión (S2-12). Entra al
    /// hash solo si tiene valor.
    /// </summary>
    public Guid? RelatedUserId { get; set; }

    /// <summary>Resultado corto (<c>ok</c>, <c>rejected</c>, <c>locked</c>…). Código, nunca un mensaje libre.</summary>
    public string Outcome { get; set; } = string.Empty;

    public string PrevHash { get; set; } = string.Empty;

    public string EntryHash { get; set; } = string.Empty;
}
