using QueryDoctor.Api.Auth;
using QueryDoctor.Application.Connections;
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
/// /connections (S1-09; RF-CFG-01 a 04, RF-CFG-07; CA-HU01.1–01.3, CA-HU02.1). Solo DBA (RNF-SEG-12) y CSRF por
/// mismo origen (UseSameOriginForms). La Api guarda y encola; el sondeo corre en el Worker (invariante 2).
/// </summary>
internal static class ConnectionEndpoints
{
    public static IEndpointRouteBuilder MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        // El acceso de Desarrollador a sus conexiones asignadas llega en S2-12 (ConnectionMember, filtro en la consulta).
        var group = app.MapGroup("/connections").RequireAuthorization(AuthConstants.DbaOnlyPolicy);

        group.MapPost("/", RegisterAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync).WithName(GetConnectionRoute);

        return app;
    }

    private const string GetConnectionRoute = "GetConnection";

    private static async Task<IResult> RegisterAsync(
        RegisterConnectionRequest? body,
        RegisterConnection useCase,
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
