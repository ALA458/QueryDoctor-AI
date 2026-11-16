using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// Doble del servicio de IA (S3-06): las pruebas de aceptación no dependen de un proveedor externo. Va debajo del
/// <c>HttpAiExplanationClient</c> real, así que se ejercen el JSON que de verdad sale del Worker, el enmascarado y el
/// validador .NET. Registra cada cuerpo enviado para comprobar qué salió (CA-HU21.1, CA-HU21.3).
/// </summary>
internal sealed class AiServiceDouble : HttpMessageHandler
{
    public static readonly Uri BaseAddress = new("http://ai-doble.test/");

    /// <summary>Perfil que informa <c>/info</c>: con perfil, el Worker puede usar la caché (E3, CA-HU09.3).</summary>
    public const string PromptVersion = "v1";
    public const string LlmProfile = "acceptance_double";

    private readonly ConcurrentQueue<string> _explainBodies = new();

    /// <summary>Respuesta a <c>POST /explain</c> según el cuerpo recibido. Por omisión, servicio no disponible.</summary>
    public Func<string, CancellationToken, Task<HttpResponseMessage>> Explain { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

    public IReadOnlyList<string> ExplainBodies => [.. _explainBodies];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.TrimEnd('/') ?? string.Empty;

        if (request.Method == HttpMethod.Get && path == "/info")
        {
            return Json(new { prompt_version = PromptVersion, llm_profile = LlmProfile });
        }

        if (request.Method == HttpMethod.Post && path == "/explain")
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            _explainBodies.Enqueue(body);
            return await Explain(body, cancellationToken);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
    };
}

/// <summary>
/// Respuestas programadas del "LLM". Solo usan lo que el Worker envió (hallazgos y fragmentos de la base de
/// conocimiento), como haría el modelo real con el prompt v1.
/// </summary>
internal static class AiResponses
{
    /// <summary>Fragmentos curados de REG-02 (migración BaseDeConocimientoInicial): interpretación y recomendación.</summary>
    public const string Reg02InterpretationFragment = "a1010002-0002-0000-0000-000000000001";
    public const string Reg02RecommendationFragment = "a1010002-0002-0000-0000-000000000002";

    /// <summary>
    /// Respuesta que el validador acepta como TRAZADA: una observación sin números por hallazgo y, si hay REG-02, una
    /// interpretación y una recomendación apoyadas en sus fragmentos curados.
    /// </summary>
    public static Task<HttpResponseMessage> Traced(string body, CancellationToken cancellationToken)
    {
        var request = RequestOf(body);
        var findings = request.GetProperty("findings").EnumerateArray()
            .Select(f => (Id: f.GetProperty("finding_id").GetString()!, Rule: f.GetProperty("rule_id").GetString()!))
            .ToList();
        var fragments = request.GetProperty("kb_fragments").EnumerateArray()
            .Select(f => f.GetProperty("fragment_id").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var claims = findings
            .Select(f => (object)new { claim_type = "OBSERVACION", statement = "El motor señaló este operador del plan.", finding_ids = new[] { f.Id } })
            .ToList();

        var reg02 = findings.Where(f => f.Rule == "REG-02").Select(f => f.Id).ToArray();
        if (reg02.Length > 0 && fragments.Contains(Reg02InterpretationFragment) && fragments.Contains(Reg02RecommendationFragment))
        {
            claims.Add(new
            {
                claim_type = "INTERPRETACION",
                statement = "El optimizador sugiere un índice que cubra las columnas del predicado.",
                finding_ids = reg02,
                kb_fragment_id = Reg02InterpretationFragment,
            });
            claims.Add(new
            {
                claim_type = "RECOMENDACION",
                statement = "Evaluar un índice no agrupado con las columnas sugeridas antes de crearlo.",
                finding_ids = reg02,
                kb_fragment_id = Reg02RecommendationFragment,
            });
        }

        return Task.FromResult(Response(request, "La consulta presenta señales del motor que conviene revisar.", claims));
    }

    /// <summary>Cita un número que no está en la evidencia: el validador debe dejarla NO_VERIFICADA (CA-HU10.3).</summary>
    public static Task<HttpResponseMessage> WrongNumber(string body, CancellationToken cancellationToken)
    {
        var request = RequestOf(body);
        var finding = request.GetProperty("findings")[0].GetProperty("finding_id").GetString()!;
        object[] claims = [new { claim_type = "OBSERVACION", statement = "El plan estima 987654 filas en este operador.", finding_ids = new[] { finding } }];
        return Task.FromResult(Response(request, "Resumen.", claims));
    }

    /// <summary>El proveedor no responde: el cliente corta en AiService:TimeoutSeconds (CA-HU12.1).</summary>
    public static async Task<HttpResponseMessage> NeverAnswers(string body, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("No se alcanza.");
    }

    private static JsonElement RequestOf(string body)
    {
        var root = JsonDocument.Parse(body).RootElement;
        return root.TryGetProperty("request", out var inner) ? inner : root;
    }

    private static HttpResponseMessage Response(JsonElement request, string summary, IEnumerable<object> claims) => AiServiceDouble.Json(new
    {
        query_id = request.GetProperty("query_id").GetString(),
        summary,
        claims,
        metadata = new
        {
            llm_profile = AiServiceDouble.LlmProfile,
            model = "acceptance-double",
            prompt_version = AiServiceDouble.PromptVersion,
            served_by = "acceptance-double",
            data_policy = "lab_only",
            execution_time_seconds = 0.01,
        },
    });
}
