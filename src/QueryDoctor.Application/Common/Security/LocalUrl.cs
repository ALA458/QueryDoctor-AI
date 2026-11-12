namespace QueryDoctor.Application.Common.Security;

/// <summary>
/// Valida que una URL de retorno sea local al sitio (evita redirecciones abiertas, OWASP A01/A07).
/// </summary>
public static class LocalUrl
{
    private const int MaxLength = 2048;

    /// <summary>
    /// Devuelve <paramref name="candidate"/> si es una ruta local ("/algo"); si no, <paramref name="fallback"/>.
    /// </summary>
    public static string Sanitize(string? candidate, string fallback = "/")
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length > MaxLength)
        {
            return fallback;
        }

        // Debe ser una ruta absoluta del mismo sitio: "/x", nunca "//host", "/\host" ni "http://...".
        if (candidate[0] != '/')
        {
            return fallback;
        }

        if (candidate.Length > 1 && (candidate[1] == '/' || candidate[1] == '\\'))
        {
            return fallback;
        }

        foreach (var c in candidate)
        {
            if (c == '\\' || char.IsControl(c))
            {
                return fallback;
            }
        }

        return candidate;
    }
}
