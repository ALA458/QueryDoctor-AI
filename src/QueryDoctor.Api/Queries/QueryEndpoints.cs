using QueryDoctor.Api.Auth;
using QueryDoctor.Application.Queries;

namespace QueryDoctor.Api.Queries;

/// <summary>
/// Endpoints de consultas y detalle (<c>GET /queries</c> y <c>GET /queries/{queryId}</c>; S1-16, S2-09, RF-DASH-01..03, RF-SCO-04, RF-SCO-06).
/// Solo DBA por ahora: la asignación de conexiones para Desarrollador llega en S2-12.
/// </summary>
internal static class QueryEndpoints
{
    public static IEndpointRouteBuilder MapQueryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/queries").RequireAuthorization(AuthConstants.DbaOnlyPolicy);

        group.MapGet("/", ListAsync);
        group.MapGet("/{queryId:guid}", GetDetailAsync);
        group.MapGet("/{queryId:guid}/plans/{planId:guid}", GetPlanTreeAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid? connectionId,
        string? window,
        string? severity,
        string? sort,
        GetQueries useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new QueryListFilter(connectionId, window, severity, sort),
            cancellationToken);

        return result switch
        {
            GetQueriesResult.Ok ok => Results.Ok(ok.Items),

            GetQueriesResult.Invalid invalid => Results.ValidationProblem(
                invalid.Errors,
                detail: "Revisa los filtros de consulta."),

            _ => throw new InvalidOperationException("Resultado de listado de consultas no contemplado."),
        };
    }

    private static async Task<IResult> GetDetailAsync(
        Guid queryId,
        string? window,
        GetQueryDetail useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(queryId, window, cancellationToken);

        return result switch
        {
            GetQueryDetailResult.Found found => Results.Ok(found.View),

            GetQueryDetailResult.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "La consulta no existe."),

            GetQueryDetailResult.Invalid invalid => Results.ValidationProblem(
                invalid.Errors,
                detail: "La ventana de tiempo solicitada no es válida."),

            _ => throw new InvalidOperationException("Resultado de detalle de consulta no contemplado."),
        };
    }

    private static async Task<IResult> GetPlanTreeAsync(
        Guid queryId,
        Guid planId,
        GetQueryPlanTree useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(queryId, planId, cancellationToken);

        return result switch
        {
            GetQueryPlanTreeResult.Found found => Results.Ok(found.View),

            GetQueryPlanTreeResult.NotFound => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "El plan o la consulta solicitados no existen."),

            _ => throw new InvalidOperationException("Resultado de árbol de plan no contemplado."),
        };
    }
}
