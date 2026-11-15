using System.Security.Claims;
using QueryDoctor.Application.Traceability;

namespace QueryDoctor.Api.Auditing;

/// <summary>
/// Puntos de auditoría de la Api (S2-06, RF-TRZ-05, CA-HU14.4). Registra usuario, tipo, objeto y resultado; nunca
/// contraseñas ni credenciales (RNF-SEG-09).
/// </summary>
internal static partial class AuditTrail
{
    public static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static string? UserName(ClaimsPrincipal principal) => principal.Identity?.Name;

    /// <summary>
    /// T7: en el login, un fallo al escribir la auditoría no bloquea la entrada (sería una denegación de servicio
    /// por la base de auditoría). Se registra el tipo de evento, sin datos, y se sigue.
    /// </summary>
    public static async Task RecordTolerantAsync(IAuditLog log, AuditEventDraft draft, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await log.AppendAsync(draft, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditWriteFailed(logger, draft.EventType, ex.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Error, Message = "No se pudo escribir el evento de auditoría {EventType}: {ErrorType}")]
    private static partial void LogAuditWriteFailed(ILogger logger, QueryDoctor.Domain.Audit.AuditEventType eventType, string errorType);
}
