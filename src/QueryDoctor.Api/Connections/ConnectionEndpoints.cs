using System.Security.Claims;
using QueryDoctor.Api.Auditing;
using QueryDoctor.Api.Auth;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Traceability;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Api.Connections;

/// <summary>Cuerpo de POST /connections (docs/CONTRATO_API_CONEXIONES.md). Los enumerados llegan como texto.</summary>
internal sealed record RegisterConnectionRequest(
    string? Platform,
    string? Server,
    string? DatabaseName,
    string? AuthenticationMethod,
    string? Encryption,
    string? Username,
    string? Password)
{
    public override string ToString() =>
        $"RegisterConnectionRequest {{ Server = {Server}, DatabaseName = {DatabaseName}, Username = {Username}, Password = ******** }}";
}

/// <summary>
/// /connections (S1-09; RF-CFG-01 a 04, RF-CFG-07; CA-HU01.1–01.3, CA-HU02.1) y su ingesta manual (S1-11; RF-ING-06,
/// RF-ING-08). Escribir es solo DBA; leer, DBA y Desarrollador con el filtro por conexión asignada (RNF-SEG-12, S2-12).
/// CSRF por mismo origen (UseSameOriginForms). La Api guarda y encola; el sondeo corre en el Worker (invariante 2).
/// </summary>
internal static class ConnectionEndpoints
{
    public static IEndpointRouteBuilder MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        // S2-12 (RNF-SEG-12): leer es de DBA y Desarrollador; el filtro por conexión de la capa de datos deja al
        // Desarrollador solo lo asignado y responde 404 por lo demás. Registrar e ingerir siguen siendo solo DBA
        // (CA-HU01.6): las dos políticas se exigen a la vez.
        var group = app.MapGroup("/connections").RequireAuthorization(AuthConstants.ConnectionReaderPolicy);

        group.MapPost("/", RegisterAsync).RequireAuthorization(AuthConstants.DbaOnlyPolicy);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync).WithName(GetConnectionRoute);

        // S1-11: ingesta manual (RF-ING-06, CA-HU03.1) y sus ejecuciones con la causa (RF-ING-08, CA-HU02.2).
        group.MapPost("/{id:guid}/ingestions", RequestIngestionAsync).RequireAuthorization(AuthConstants.DbaOnlyPolicy);
        group.MapGet("/{id:guid}/ingestions", ListIngestionsAsync);

        return app;
    }

    private const string GetConnectionRoute = "GetConnection";

    private static async Task<IResult> RegisterAsync(
        RegisterConnectionRequest? body,
        RegisterConnection useCase,
        ClaimsPrincipal principal,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (body is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Falta el cuerpo de la solicitud.");
        }

        var result = await useCase.ExecuteAsync(
            new RegisterConnectionCommand(
                body.Platform,
                body.Server,
                body.DatabaseName,
                body.AuthenticationMethod,
                body.Encryption,
                body.Username,
                body.Password),
            cancellationToken);

        if (result is RegisterConnectionResult.Accepted registered)
        {
            // S2-06 (RF-TRZ-05): registro y edición (re-registro, D3) con usuario. Sin servidor ni credenciales.
            await audit.AppendAsync(
                new AuditEventDraft(
                    registered.ReplacedPrevious ? AuditEventType.ConnectionReRegistered : AuditEventType.ConnectionRegistered,
                    AuditTrail.UserId(principal),
                    AuditTrail.UserName(principal),
                    AuditEventDraft.OutcomeOk,
                    AuditEventDraft.SubjectConnection,
                    registered.ConnectionId),
                cancellationToken);
        }

        return result switch
        {
            RegisterConnectionResult.Accepted accepted => Results.AcceptedAtRoute(
                GetConnectionRoute,
                new { id = accepted.ConnectionId },
                new { id = accepted.ConnectionId, status = ConnectionStatus.Pending, replacedPrevious = accepted.ReplacedPrevious }),

            RegisterConnectionResult.Invalid invalid => Results.ValidationProblem(
                invalid.Errors,
                detail: "Revisa los datos de la conexión."),

            RegisterConnectionResult.Conflict conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conexión ya registrada",
                detail: "Esta base ya está registrada en ese servidor.",
                extensions: new Dictionary<string, object?>
                {
                    ["existingConnectionId"] = conflict.ExistingConnectionId,
                    ["existingStatus"] = conflict.ExistingStatus.ToString(),
                }),

            _ => throw new InvalidOperationException("Resultado de alta no contemplado."),
        };
    }

    private const int IngestionRunsShown = 20;

    private static async Task<IResult> RequestIngestionAsync(
        Guid id,
        RequestIngestion useCase,
        ClaimsPrincipal principal,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(id, cancellationToken);
        if (result is RequestIngestionResult.Accepted)
        {
            // S2-06 (RF-TRZ-05): ingesta manual con usuario.
            await audit.AppendAsync(
                new AuditEventDraft(
                    AuditEventType.ManualIngestionRequested,
                    AuditTrail.UserId(principal),
                    AuditTrail.UserName(principal),
                    AuditEventDraft.OutcomeOk,
                    AuditEventDraft.SubjectConnection,
                    id),
                cancellationToken);
        }

        return result switch
        {
            RequestIngestionResult.Accepted accepted => Results.Accepted(
                $"/connections/{accepted.ConnectionId}/ingestions",
                new { connectionId = accepted.ConnectionId }),

            RequestIngestionResult.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "La conexión no existe."),

            RequestIngestionResult.NotActive notActive => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conexión no activa",
                detail: "Solo se puede ingerir una conexión activa.",
                extensions: new Dictionary<string, object?> { ["status"] = notActive.Status.ToString() }),

            RequestIngestionResult.AlreadyInProgress => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ingesta en curso",
                detail: IngestionCauseMessages.For(IngestionCause.AlreadyRunning),
                extensions: new Dictionary<string, object?> { ["cause"] = nameof(IngestionCause.AlreadyRunning) }),

            _ => throw new InvalidOperationException("Resultado de ingesta no contemplado."),
        };
    }

    private static async Task<IResult> ListIngestionsAsync(Guid id, IIngestionQueries queries, CancellationToken cancellationToken)
    {
        var runs = await queries.ListRunsAsync(id, IngestionRunsShown, cancellationToken);
        return runs is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "La conexión no existe.")
            : Results.Ok(runs);
    }

    private static async Task<IResult> ListAsync(IConnectionQueries queries, CancellationToken cancellationToken) =>
        Results.Ok(await queries.ListAsync(cancellationToken));

    private static async Task<IResult> GetAsync(Guid id, IConnectionQueries queries, CancellationToken cancellationToken)
    {
        var detail = await queries.GetAsync(id, cancellationToken);
        return detail is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "La conexión no existe.")
            : Results.Ok(detail);
    }
}
