using QueryDoctor.Api.Auth;
using QueryDoctor.Application.Explanations;

namespace QueryDoctor.Api.Explanations;

/// <summary>
/// Explicaciones de una consulta (S2-05, CU-05). La Api solo guarda la solicitud y encola <c>explain</c>; el contexto,
/// el enmascarado y la llamada a la IA ocurren en el Worker (invariante 2). Solo DBA por ahora: el acceso de
/// Desarrollador a sus conexiones llega en S2-12.
/// </summary>
internal static class ExplanationEndpoints
{
    public const string LatestRoute = "latest";

    public static IEndpointRouteBuilder MapExplanationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/queries/{queryId:guid}/explanations").RequireAuthorization(AuthConstants.DbaOnlyPolicy);

        group.MapPost("/", RequestAsync);
        group.MapGet($"/{LatestRoute}", GetLatestAsync);

        return app;
    }

    private static async Task<IResult> RequestAsync(Guid queryId, RequestExplanation useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(queryId, cancellationToken);

        return result switch
        {
            RequestExplanationResult.Accepted accepted => Results.Accepted(
                $"/queries/{queryId}/explanations/{LatestRoute}",
                new { explanation_id = accepted.ExplanationId, status = ExplanationCodes.StatePending }),

            // CA-HU09.2: el sistema no genera una explicación y lo indica.
            RequestExplanationResult.NoFindings => Results.Ok(
                new { status = ExplanationCodes.StateNoFindings, notice = ExplanationCodes.NoFindingsNotice }),

            RequestExplanationResult.QueryNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "La consulta no existe."),

            RequestExplanationResult.AlreadyInProgress inProgress => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Explicación en curso",
                detail: "Ya hay una explicación pendiente o en curso para esta consulta.",
                extensions: new Dictionary<string, object?>
                {
                    ["cause"] = "AlreadyInProgress",
                    ["explanation_id"] = inProgress.ExplanationId,
                }),

            _ => throw new InvalidOperationException("Resultado de explicación no contemplado."),
        };
    }

    private static async Task<IResult> GetLatestAsync(Guid queryId, GetLatestExplanation useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(queryId, cancellationToken);

        return result switch
        {
            GetLatestExplanationResult.Found found => Results.Ok(found.View),

            GetLatestExplanationResult.QueryNotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "La consulta no existe."),

            GetLatestExplanationResult.NotRequested => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "Todavía no se pidió una explicación para esta consulta.",
                extensions: new Dictionary<string, object?> { ["cause"] = "NotRequested" }),

            _ => throw new InvalidOperationException("Resultado de explicación no contemplado."),
        };
    }
}
