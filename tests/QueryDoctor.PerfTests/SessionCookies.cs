namespace QueryDoctor.PerfTests;

/// <summary>
/// La cookie de sesión de la Api es <c>Secure</c> (CookieSecurePolicy.Always) y el laboratorio se mide por
/// http://127.0.0.1:8080, así que un CookieContainer no la reenviaría. Se arma la cabecera Cookie a mano, como haría el
/// navegador en localhost. Solo nombre=valor: los atributos (Path, Secure, HttpOnly...) no se reenvían.
/// </summary>
public static class SessionCookies
{
    /// <summary>Une las cabeceras Set-Cookie en un valor de cabecera Cookie; null si no hay ninguna.</summary>
    public static string? ToCookieHeader(IEnumerable<string> setCookieHeaders)
    {
        ArgumentNullException.ThrowIfNull(setCookieHeaders);

        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in setCookieHeaders)
        {
            var pair = header.Split(';', 2)[0].Trim();
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            // Una cookie vaciada (logout, cambio de sello) reemplaza a la anterior: la última gana.
            pairs[pair[..eq]] = pair[(eq + 1)..];
        }

        var kept = pairs.Where(p => p.Value.Length > 0).Select(p => $"{p.Key}={p.Value}").ToList();
        return kept.Count == 0 ? null : string.Join("; ", kept);
    }
}
