namespace QueryDoctor.Application.Common.Security;

/// <summary>
/// Defensa adicional contra CSRF en los formularios de autenticación (se suma a la cookie SameSite=Strict).
/// Primero se confía en Sec-Fetch-Site, que el navegador fija y la página no puede alterar; si no viene,
/// se compara el host de la cabecera Origin con el del propio sitio.
/// </summary>
public static class OriginCheck
{
    /// <summary>
    /// Sin ninguna de las dos cabeceras se permite (clientes que no son navegador, como curl).
    /// </summary>
    public static bool IsAllowed(string? originHeader, string requestHost, string? secFetchSite = null)
    {
        if (!string.IsNullOrEmpty(secFetchSite))
        {
            // "none" = navegación directa del usuario; "same-origin" = el propio sitio.
            return secFetchSite is "same-origin" or "none";
        }

        if (string.IsNullOrEmpty(originHeader))
        {
            return true;
        }

        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out var origin))
        {
            return false;
        }

        return string.Equals(origin.Host, requestHost, StringComparison.OrdinalIgnoreCase);
    }
}
