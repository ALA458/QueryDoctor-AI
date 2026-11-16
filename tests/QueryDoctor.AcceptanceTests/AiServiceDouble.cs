using System.Collections.Concurrent;
using System.Net;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// Doble del servicio de IA (S3-06): las pruebas de aceptación no dependen de un proveedor externo. Va debajo del
/// <c>HttpAiExplanationClient</c> real, así que se ejercen el JSON que de verdad sale del Worker, el enmascarado y el
/// validador .NET. Registra cada cuerpo enviado para comprobar qué salió (CA-HU21.1, CA-HU21.3).
/// </summary>
internal sealed class AiServiceDouble : HttpMessageHandler
{
    public static readonly Uri BaseAddress = new("http://ai-doble.test/");

    private readonly ConcurrentQueue<string> _explainBodies = new();

    /// <summary>Respuesta a <c>POST /explain</c> según el cuerpo recibido. Por omisión, servicio no disponible.</summary>
    public Func<string, HttpResponseMessage> Explain { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

    /// <summary>Respuesta a <c>GET /info</c>. Por omisión, no disponible: sin perfil no se usa la caché (E3).</summary>
    public Func<HttpResponseMessage> Info { get; set; } = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

    public IReadOnlyList<string> ExplainBodies => [.. _explainBodies];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.TrimEnd('/') ?? string.Empty;

        if (request.Method == HttpMethod.Get && path == "/info")
        {
            return Info();
        }

        if (request.Method == HttpMethod.Post && path == "/explain")
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            _explainBodies.Enqueue(body);
            return Explain(body);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
