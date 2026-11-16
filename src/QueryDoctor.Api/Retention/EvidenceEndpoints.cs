using System.Security.Claims;
using QueryDoctor.Api.Auditing;
using QueryDoctor.Api.Auth;
using QueryDoctor.Application.Retention;

namespace QueryDoctor.Api.Retention;

/// <summary>
/// Borrado a pedido del DBA (S2-13, RNF-SEG-10): texto SQL, planes y evidencia de una consulta o de una conexión entera,
/// sin esperar al plazo de retención. Solo DBA y CSRF por mismo origen (UseSameOriginForms cubre DELETE). Responde 204
/// también si ya no quedaba nada (idempotente) y siempre deja su evento DataDeleted con el usuario, en la misma
/// transacción que el borrado (RF-TRZ-05). No borra la conexión ni la consulta: solo su evidencia.
/// </summary>
internal static class EvidenceEndpoints
{
    public static IEndpointRouteBuilder MapEvidenceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/queries/{queryId:guid}/evidence", DeleteQueryEvidenceAsync)
            .RequireAuthorization(AuthConstants.DbaOnlyPolicy);

        // Como las demás escrituras de /connections: ConnectionReader del grupo más DbaOnly encima (S2-12).
        app.MapDelete("/connections/{connectionId:guid}/evidence", DeleteConnectionEvidenceAsync)
            .RequireAuthorization(AuthConstants.ConnectionReaderPolicy)
            .RequireAuthorization(AuthConstants.DbaOnlyPolicy);

        return app;
    }

    private static Task<IResult> DeleteQueryEvidenceAsync(
        Guid queryId,
        DeleteEvidence useCase,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        DeleteAsync(EvidenceScope.ForQuery(queryId), "La consulta no existe.", useCase, principal, cancellationToken);

    private static Task<IResult> DeleteConnectionEvidenceAsync(
        Guid connectionId,
        DeleteEvidence useCase,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        DeleteAsync(EvidenceScope.ForConnection(connectionId), "La conexión no existe.", useCase, principal, cancellationToken);

    private static async Task<IResult> DeleteAsync(
        EvidenceScope scope,
        string notFoundDetail,
        DeleteEvidence useCase,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var actor = new EvidenceDeletionActor(AuditTrail.UserId(principal), AuditTrail.UserName(principal));
        var result = await useCase.ExecuteAsync(scope, actor, cancellationToken);

        return result switch
        {
            DeleteEvidenceResult.Deleted => Results.NoContent(),
            DeleteEvidenceResult.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, detail: notFoundDetail),
            _ => throw new InvalidOperationException("Resultado de borrado de evidencia no contemplado."),
        };
    }
}
