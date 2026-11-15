using System.Net;
using System.Text;
using QueryDoctor.Application.Ai;
using QueryDoctor.Domain.Ai;

namespace QueryDoctor.Infrastructure.Ai;

/// <summary>
/// Configuración del cliente hacia el servicio de IA (sección <c>AiService</c>). No hay ninguna opción sobre el
/// enmascarado: la solicitud ya llega enmascarada por tipo (CA-HU21.4).
/// </summary>
public sealed class AiServiceOptions
{
    public const string SectionName = "AiService";

    /// <summary>
    /// Tiempo total por defecto (E1): el servicio de IA ya hace 60 s × (1 + 2 reintentos) hacia el LLM
    /// (RNF-FIA-02), más margen. Este lado no reintenta.
    /// </summary>
    public const int DefaultTimeoutSeconds = 200;

    /// <summary>Tamaño máximo de la respuesta que se lee. Una mayor se trata como salida no válida.</summary>
    public const int DefaultMaxResponseBytes = 1_048_576;

    /// <summary>Dirección del servicio en la red backend, por ejemplo <c>http://ai:8000/</c>.</summary>
    public string? BaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    public int MaxResponseBytes { get; set; } = DefaultMaxResponseBytes;

    public void Validate()
    {
        if (TimeoutSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(TimeoutSeconds)} debe estar entre 1 y 3600.");
        }

        if (MaxResponseBytes is < 1024 or > 16 * 1024 * 1024)
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(MaxResponseBytes)} fuera de rango.");
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException($"{SectionName}:{nameof(BaseUrl)} debe ser una URL http o https absoluta.");
        }
    }
}

/// <summary>
/// Cliente HTTP de <c>POST /explain</c> (S1-18). Solo corre en el Worker (invariante 2) y solo envía
/// <see cref="MaskedExplanationRequest"/>. Un único intento con tiempo total (E1); cualquier fallo se traduce a
/// <see cref="AiServiceUnavailableException"/> con causa tipada, sin el cuerpo ni el mensaje (RNF-SEG-09).
/// </summary>
public sealed class HttpAiExplanationClient : IAiExplanationClient
{
    private const string ExplainPath = "explain";

    private readonly HttpClient _http;
    private readonly AiServiceOptions _options;

    public HttpAiExplanationClient(HttpClient http, AiServiceOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public async Task<string> SendAsync(MaskedExplanationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new AiServiceUnavailableException(ExplanationCause.ServiceUnreachable);
        }

        var endpoint = new Uri(new Uri(EnsureTrailingSlash(_options.BaseUrl)), ExplainPath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var content = new StringContent(request.ToWireJson(), Encoding.UTF8, "application/json");
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            if (request.CorrelationId is { } correlationId)
            {
                message.Headers.Add("X-Correlation-ID", correlationId.ToString("D"));
            }

            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiServiceUnavailableException(CauseFor(response.StatusCode));
            }

            return await ReadLimitedAsync(response.Content, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiServiceUnavailableException(ExplanationCause.Timeout, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new AiServiceUnavailableException(ExplanationCause.ServiceUnreachable, ex);
        }
    }

    internal static ExplanationCause CauseFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.UnprocessableEntity => ExplanationCause.InvalidOutput,
        HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout => ExplanationCause.Timeout,
        >= HttpStatusCode.InternalServerError => ExplanationCause.ProviderError,
        _ => ExplanationCause.Unexpected,
    };

    private async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[_options.MaxResponseBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        if (total > _options.MaxResponseBytes)
        {
            throw new AiServiceUnavailableException(ExplanationCause.InvalidOutput);
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    /// <summary>
    /// <see cref="HttpClient"/> único para el servicio de IA (E8, sin paquetes nuevos). El tiempo límite lo maneja
    /// <see cref="SendAsync"/>; aquí queda infinito para no cortar antes.
    /// </summary>
    public static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AllowAutoRedirect = false,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
}
