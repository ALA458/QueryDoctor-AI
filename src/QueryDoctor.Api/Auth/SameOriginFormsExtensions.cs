using QueryDoctor.Application.Common.Security;

namespace QueryDoctor.Api.Auth;

/// <summary>
/// Rechaza con 403 los POST a /auth/* que no vienen del propio sitio. Es una defensa adicional contra CSRF:
/// el control principal es la cookie SameSite=Strict. Vive en un solo punto, antes de cualquier endpoint.
/// </summary>
internal static class SameOriginFormsExtensions
{
    public static IApplicationBuilder UseSameOriginForms(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var request = context.Request;
            var isAuthPost = HttpMethods.IsPost(request.Method) && request.Path.StartsWithSegments("/auth");

            if (isAuthPost && !OriginCheck.IsAllowed(
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
