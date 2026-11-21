using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace QueryDoctor.PerfTests;

/// <summary>Una ejecución de <c>GET /connections/{id}/ingestions</c> (IngestionRunView).</summary>
public sealed record IngestionRunDto(
    Guid Id,
    string Trigger,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int? RowsUpserted,
    string? Cause);

/// <summary>
/// Cliente de la Api pública a través de Nginx (<c>{base}/api/...</c>), con los mismos pasos que la SPA: login por
/// formulario, cookie de sesión, JSON. Nunca escribe contraseñas en consola, archivos ni mensajes de error.
/// </summary>
public sealed class PlatformClient : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private string? _cookie;

    public PlatformClient(Uri baseUrl, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);

        // Sin cookies automáticas ni redirecciones: la cookie es Secure (ver SessionCookies) y las respuestas del login
        // son redirecciones que hay que leer, no seguir.
        _http = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(baseUrl.ToString().TrimEnd('/') + "/api/"),
            Timeout = requestTimeout,
        };
    }

    /// <summary>
    /// Entra con usuario y contraseña. Devuelve si la cuenta debe cambiar la contraseña (sesión restringida) o lanza si
    /// el login fue rechazado.
    /// </summary>
    public async Task<bool> LoginAsync(string userName, string password, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["usuario"] = userName,
            ["password"] = password,
        });
        using var response = await SendAsync(HttpMethod.Post, "auth/login", content, cancellationToken);

        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        if (response.StatusCode != HttpStatusCode.Redirect || location.Contains("error=", StringComparison.Ordinal))
        {
            var why = location.Contains("error=locked", StringComparison.Ordinal) ? "cuenta bloqueada temporalmente" : "credenciales rechazadas";
            throw new InvalidOperationException($"El login de '{userName}' falló ({why}, HTTP {(int)response.StatusCode}).");
        }

        _cookie = response.Headers.TryGetValues("Set-Cookie", out var setCookies) ? SessionCookies.ToCookieHeader(setCookies) : null;
        if (_cookie is null)
        {
            throw new InvalidOperationException($"El login de '{userName}' no devolvió cookie de sesión.");
        }

        return location.Contains("cambiar-contrasena", StringComparison.Ordinal);
    }

    /// <summary>Cambia la contraseña de la sesión actual. La Api cierra la sesión al terminar: hay que volver a entrar.</summary>
    public async Task ChangePasswordAsync(string current, string next, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["actual"] = current,
            ["nueva"] = next,
            ["confirmar"] = next,
        });
        using var response = await SendAsync(HttpMethod.Post, "auth/change-password", content, cancellationToken);

        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        if (response.StatusCode != HttpStatusCode.Redirect || !location.Contains("changed=1", StringComparison.Ordinal))
        {
            var cause = location.Contains("error=", StringComparison.Ordinal) ? location[(location.IndexOf("error=", StringComparison.Ordinal) + 6)..] : "desconocida";
            throw new InvalidOperationException(
                $"El cambio de contraseña falló (causa: {cause}). La nueva debe cumplir la política: 12+ caracteres con mayúsculas, minúsculas, números y símbolos.");
        }

        _cookie = null;
    }

    /// <summary>Registra una conexión con autenticación SQL y TLS obligatorio (RNF-SEG-03). Devuelve su id.</summary>
    public async Task<Guid> RegisterConnectionAsync(string server, string database, string user, string password, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(new
        {
            platform = "SqlServer",
            server,
            databaseName = database,
            authenticationMethod = "SqlAuthentication",
            encryption = "Mandatory",
            username = user,
            password,
        });
        using var response = await SendAsync(HttpMethod.Post, "connections", content, cancellationToken);

        if (response.StatusCode is not HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException($"POST /connections respondió {(int)response.StatusCode}; se esperaba 202 con la base de PostgreSQL limpia.");
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>Estado de la conexión (<c>Pending</c>, <c>Active</c>, ...).</summary>
    public async Task<string> GetConnectionStatusAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        var body = await GetJsonAsync<JsonElement>($"connections/{connectionId}", cancellationToken);
        return body.GetProperty("status").GetString() ?? string.Empty;
    }

    public Task<IReadOnlyList<IngestionRunDto>> ListIngestionRunsAsync(Guid connectionId, CancellationToken cancellationToken) =>
        GetJsonAsync<IReadOnlyList<IngestionRunDto>>($"connections/{connectionId}/ingestions", cancellationToken);

    /// <summary>Pide una ingesta manual (<c>POST /connections/{id}/ingestions</c>, 202).</summary>
    public async Task RequestIngestionAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, $"connections/{connectionId}/ingestions", null, cancellationToken);
        if (response.StatusCode is not HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException($"POST /connections/{connectionId}/ingestions respondió {(int)response.StatusCode}; se esperaba 202.");
        }
    }

    public async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (response.StatusCode is not HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"GET /{path} respondió {(int)response.StatusCode}.");
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
            ?? throw new InvalidOperationException($"GET /{path} devolvió un cuerpo vacío.");
    }

    /// <summary>Envía una solicitud con la cookie de sesión. El llamador libera la respuesta.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        if (_cookie is not null)
        {
            request.Headers.Add("Cookie", _cookie);
        }

        return await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
    }

    public void Dispose() => _http.Dispose();
}
