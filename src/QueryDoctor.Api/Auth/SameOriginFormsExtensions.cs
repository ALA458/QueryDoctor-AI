using QueryDoctor.Application.Common.Security;

namespace QueryDoctor.Api.Auth;

/// <summary>
/// Rechaza con 403 las peticiones que cambian estado (POST, PUT, PATCH, DELETE) que no vienen del propio sitio.
/// Es una defensa adicional contra CSRF: el control principal es la cookie SameSite=Strict. Vive en un solo punto,
/// antes de cualquier endpoint.
/// </summary>
/// <remarks>
/// S1-09: antes cubría solo /auth/*. Se extendió a todo método que cambia estado (POST /connections y los que
/// vengan) para que un endpoint nuevo quede protegido por defecto y no dependa de acordarse de agregarlo.
/// </remarks>
internal static class SameOriginFormsExtensions
{
    public static IApplicationBuilder UseSameOriginForms(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var changesState = HttpMethods.IsPost(request.Method)
                || HttpMethods.IsPut(request.Method)
                || HttpMethods.IsPatch(request.Method)
                || HttpMethods.IsDelete(request.Method);

            if (changesState && !OriginCheck.IsAllowed(
                    request.Headers.Origin.ToString(),
                    request.Host.Host,
                    request.Headers["Sec-Fetch-Site"].ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });
}
