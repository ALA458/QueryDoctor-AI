using System.Security.Claims;
using QueryDoctor.Api.Auditing;
using QueryDoctor.Api.Auth;
using QueryDoctor.Application.Connections;
using QueryDoctor.Domain.Ops;

namespace QueryDoctor.Api.Connections;

/// <summary>
/// Asignación de conexiones a Desarrolladores (S2-12, RNF-SEG-12). Solo DBA y CSRF por mismo origen
/// (UseSameOriginForms cubre PUT y DELETE). Cada cambio real queda en la auditoría (RF-TRZ-05) en la misma transacción
/// que el cambio, con la conexión como objeto y el Desarrollador como usuario relacionado; repetir una asignación
/// existente no deja un segundo evento.
/// </summary>
internal static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/connections/{connectionId:guid}/assignments").RequireAuthorization(AuthConstants.DbaOnlyPolicy);
        group.MapGet("/", ListAsync);
        group.MapPut("/{userId:guid}", AssignAsync);
        group.MapDelete("/{userId:guid}", UnassignAsync);

        // Para elegir a quién asignar. Solo existe el filtro por rol Desarrollador: no es una gestión de usuarios.
        app.MapGet("/users", ListUsersAsync).RequireAuthorization(AuthConstants.DbaOnlyPolicy);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid connectionId, ManageConnectionAssignments useCase, CancellationToken cancellationToken)
    {
        var assignments = await useCase.ListAsync(connectionId, cancellationToken);
        return assignments is null ? ConnectionNotFound() : Results.Ok(assignments);
    }

    private static async Task<IResult> AssignAsync(
        Guid connectionId,
        Guid userId,
        ManageConnectionAssignments useCase,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var result = await useCase.AssignAsync(connectionId, userId, Actor(principal), cancellationToken);

        return result switch
        {
            AssignConnectionResult.Assigned => Results.NoContent(),
            AssignConnectionResult.ConnectionNotFound => ConnectionNotFound(),
            AssignConnectionResult.UserNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "El usuario no existe."),
            AssignConnectionResult.NotDeveloper => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: "Solo se asignan conexiones a usuarios con rol Desarrollador.",
                extensions: new Dictionary<string, object?> { ["cause"] = "NotDeveloper" }),
            _ => throw new InvalidOperationException("Resultado de asignación no contemplado."),
        };
    }

    private static async Task<IResult> UnassignAsync(
        Guid connectionId,
        Guid userId,
        ManageConnectionAssignments useCase,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        await useCase.UnassignAsync(connectionId, userId, Actor(principal), cancellationToken)
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "La asignación no existe.");

    private static async Task<IResult> ListUsersAsync(string? role, ManageConnectionAssignments useCase, CancellationToken cancellationToken)
    {
        if (!string.Equals(role, RoleNames.Developer, StringComparison.Ordinal))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                detail: $"Indica role={RoleNames.Developer}: es el único filtro disponible.");
        }

        return Results.Ok(await useCase.ListDevelopersAsync(cancellationToken));
    }

    // S2-06 (T7): la auditoría es estricta. Aquí va en la misma transacción que el cambio (la escribe el store).
    private static AssignmentActor Actor(ClaimsPrincipal principal) =>
        new(AuditTrail.UserId(principal), AuditTrail.UserName(principal));

    private static IResult ConnectionNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: "La conexión no existe.");
}
