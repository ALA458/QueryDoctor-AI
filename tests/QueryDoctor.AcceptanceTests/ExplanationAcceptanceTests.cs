using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QueryDoctor.LabTests;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// HU-09 a HU-12, HU-14 y HU-21 de punta a punta: la Api encola explain, el Worker descifra, enmascara, arma el contexto,
/// llama al doble de IA por HTTP, valida en .NET y guarda; la SPA lo lee por la Api. La IA es un doble: lo que se prueba
/// es la plataforma alrededor del modelo (las métricas del modelo real son el arnés S3-02).
/// </summary>
[Collection(AcceptanceGroup.Name)]
[Trait("Suite", "Aceptacion")]
public sealed class ExplanationAcceptanceTests(AcceptanceFixture platform)
{
    private const string PersistentNotice = "Trazada a evidencia del motor; no equivale a un diagnóstico confirmado";

    // CA-HU09.1: afirmaciones con sus referencias. CA-HU10.1: cada una lleva hallazgos y nodos del plan. CA-HU09.4:
    // la interpretación sobre evidencia estimada se etiqueta como indicio. CA-HU11.4: aviso persistente de TRAZADA.
    // CA-HU11.1 y CA-HU11.3: la recomendación pide revisión humana y advierte el costo de escritura del índice.
    [LabFact]
    [Trait("Criterio", "CA-HU09.1")]
    [Trait("Criterio", "CA-HU09.4")]
    [Trait("Criterio", "CA-HU10.1")]
    [Trait("Criterio", "CA-HU11.1")]
    [Trait("Criterio", "CA-HU11.3")]
    [Trait("Criterio", "CA-HU11.4")]
    public async Task UnaExplicacionTrazadaEnlazaHallazgosYNodosConSusAvisos()
    {
        var (lab, queryId) = await Esc03Async();

        var explanation = await ExplainAsync(lab, queryId, AiResponses.Traced);

        Assert.True(explanation.GetProperty("status").GetString() == "COMPLETADA", $"Explicación: {explanation}");
        Assert.Equal("TRAZADA", explanation.GetProperty("validation_status").GetString());
        Assert.Equal(PersistentNotice, explanation.GetProperty("notice").GetString());

        var detail = await AnalysisAcceptanceTests.DetailAsync(lab, queryId);
        var findingCodes = detail.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("findingCode").GetString()).ToHashSet();
        var claims = explanation.GetProperty("claims").EnumerateArray().ToList();
        Assert.NotEmpty(claims);
        Assert.All(claims, claim =>
        {
            Assert.True(claim.GetProperty("passed").GetBoolean());
            var references = claim.GetProperty("references").EnumerateArray().ToList();
            Assert.NotEmpty(references);
            Assert.All(references, r => Assert.Contains(r.GetProperty("finding_code").GetString(), findingCodes));
        });
        Assert.Contains(claims, c => c.GetProperty("references").EnumerateArray().Any(r => r.GetProperty("node_ids").GetArrayLength() > 0));

        var interpretation = Assert.Single(claims, c => c.GetProperty("claim_type").GetString() == "INTERPRETACION");
        Assert.Equal("indicio", interpretation.GetProperty("indication_label").GetString());

        var recommendation = Assert.Single(claims, c => c.GetProperty("claim_type").GetString() == "RECOMENDACION");
        Assert.True(recommendation.GetProperty("requires_human_review").GetBoolean());
        Assert.True(recommendation.GetProperty("write_cost_warning").GetBoolean());
    }

    // CA-HU21.1: lo que salió hacia la IA lleva marcadores y ninguno de los literales del SQL. CA-HU21.3: no lleva la
    // cuenta, la contraseña ni la cadena de conexión de la base objetivo.
    [LabFact]
    [Trait("Criterio", "CA-HU21.1")]
    [Trait("Criterio", "CA-HU21.3")]
    public async Task LoQueSaleHaciaLaIaEstaEnmascaradoYSinCredenciales()
    {
        // ACC02 (lab/sql/12) solo se explica aquí: así la caché no evita la llamada y hay un cuerpo que revisar.
        // Su texto: SELECT /* QD-ACC:ACC02 */ … WHERE SkewKey = @k OPTION (USE HINT('QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150'))
        var lab = await platform.QdLabAsync();
        var queryId = (await AnalysisAcceptanceTests.QueriesAsync(lab, "window=24h")).EnumerateArray()
            .First(q => q.GetProperty("sqlPreview").GetString() is { } sql
                && sql.Contains("SUM(Amount)", StringComparison.Ordinal)
                && sql.Contains("SkewKey", StringComparison.Ordinal)
                && q.GetProperty("findings").GetArrayLength() > 0)
            .GetProperty("id").GetGuid();
        var sent = platform.Ai.ExplainBodies.Count;

        await ExplainAsync(lab, queryId, AiResponses.Traced, allowCache: false);

        var body = Assert.Single(platform.Ai.ExplainBodies.Skip(sent));
        using var json = JsonDocument.Parse(body);
        var request = json.RootElement.TryGetProperty("request", out var inner) ? inner : json.RootElement;
        var sqlText = request.GetProperty("sql_text").GetString()!;

        Assert.Contains("@qd_str_", sqlText, StringComparison.Ordinal);
        Assert.DoesNotContain("QUERY_OPTIMIZER_COMPATIBILITY_LEVEL_150", body, StringComparison.Ordinal);
        Assert.DoesNotContain("QD-ACC", body, StringComparison.Ordinal); // los comentarios no viajan

        Assert.DoesNotContain(Platform.LabPassword, body, StringComparison.Ordinal);
        Assert.DoesNotContain(LabEnvironment.Reader, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AcceptanceFixture.Server + ",", body, StringComparison.Ordinal);
    }

    // CA-HU09.3: la misma explicación con el mismo contexto sale de caché en ≤ 2 s, sin volver a llamar a la IA.
    [LabFact]
    [Trait("Criterio", "CA-HU09.3")]
    public async Task ConElMismoContextoSaleDeCacheEnDosSegundos()
    {
        var (lab, queryId) = await Esc03Async();
        await ExplainAsync(lab, queryId, AiResponses.Traced);
        var sent = platform.Ai.ExplainBodies.Count;

        var clock = Stopwatch.StartNew();
        var cached = await ExplainAsync(lab, queryId, AiResponses.Traced);
        clock.Stop();

        Assert.True(cached.GetProperty("from_cache").GetBoolean(), $"No salió de caché: {cached}");
        Assert.Equal("TRAZADA", cached.GetProperty("validation_status").GetString());
        Assert.Equal(sent, platform.Ai.ExplainBodies.Count);
        Assert.True(clock.Elapsed <= TimeSpan.FromSeconds(2), $"Desde caché tardó {clock.Elapsed.TotalMilliseconds:F0} ms.");
    }

    // CA-HU10.3: un número citado que no coincide con la evidencia hace fallar la validación. CA-HU10.2: la explicación
    // se muestra NO_VERIFICADA, nunca como trazada.
    [LabFact]
    [Trait("Criterio", "CA-HU10.2")]
    [Trait("Criterio", "CA-HU10.3")]
    public async Task UnNumeroQueNoCoincideDejaLaExplicacionNoVerificada()
    {
        var lab = await platform.QdLabAsync();
        var queryId = await AnalysisAcceptanceTests.Esc01Async(lab);

        var explanation = await ExplainAsync(lab, queryId, AiResponses.WrongNumber, allowCache: false);

        Assert.True(explanation.GetProperty("status").GetString() == "COMPLETADA", $"Explicación: {explanation}");
        Assert.Equal("NO_VERIFICADA", explanation.GetProperty("validation_status").GetString());
        Assert.NotEqual(PersistentNotice, explanation.GetProperty("notice").GetString());
        Assert.Contains(explanation.GetProperty("validation_failures").EnumerateArray(), f => f.GetProperty("code").GetString() == "NumericMismatch");
    }

    // CA-HU12.1: si la IA no responde a tiempo se ven los hallazgos y un aviso de indisponibilidad. CA-HU12.2: el resto
    // de la plataforma sigue funcionando.
    [LabFact]
    [Trait("Criterio", "CA-HU12.1")]
    [Trait("Criterio", "CA-HU12.2")]
    public async Task SiLaIaNoRespondeSeVenLosHallazgosYLoDemasSigueFuncionando()
    {
        var lab = await platform.QdLabAsync();
        var queryId = await AnalysisAcceptanceTests.QueryWithRuleAsync(lab, "REG-04", "CustomerId BETWEEN");

        var explanation = await ExplainAsync(lab, queryId, AiResponses.NeverAnswers, allowCache: false);

        Assert.True(explanation.GetProperty("status").GetString() == "IA_NO_DISPONIBLE", $"Explicación: {explanation}");
        Assert.Equal("Timeout", explanation.GetProperty("cause").GetString());
        Assert.False(string.IsNullOrWhiteSpace(explanation.GetProperty("notice").GetString()));
        Assert.NotEqual(0, explanation.GetProperty("findings").GetArrayLength());

        var detail = await AnalysisAcceptanceTests.DetailAsync(lab, queryId);
        var planId = detail.GetProperty("plans")[0].GetProperty("planId").GetGuid();
        await Platform.GetJsonAsync(lab.Dba, $"/queries?connectionId={lab.Id}");
        await Platform.GetJsonAsync(lab.Dba, $"/queries/{queryId}/plans/{planId}");
        await Platform.GetJsonAsync(lab.Dba, $"/connections/{lab.Id}");
        await Platform.GetJsonAsync(lab.Dba, "/trace?take=5");
    }

    // CA-HU09.2: sin hallazgos no se genera explicación y se indica, sin llamar a la IA.
    [LabFact]
    [Trait("Criterio", "CA-HU09.2")]
    public async Task SinHallazgosNoSeGeneraExplicacionYSeIndica()
    {
        var lab = await platform.QdLabAsync();
        var queries = await AnalysisAcceptanceTests.QueriesAsync(lab, "window=24h");
        var withoutFindings = queries.EnumerateArray().First(q => q.GetProperty("findings").GetArrayLength() == 0).GetProperty("id").GetGuid();
        var sent = platform.Ai.ExplainBodies.Count;

        using var response = await lab.Dba.PostAsync($"/queries/{withoutFindings}/explanations", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SIN_HALLAZGOS", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("notice").GetString()));
        Assert.Equal(sent, platform.Ai.ExplainBodies.Count);
    }

    // CA-HU14.1: la entrada de traza de la explicación lleva hallazgos, versión del catálogo, modelo, versión del prompt,
    // hash del contexto, fecha y resultado de la validación; la cadena sigue íntegra.
    [LabFact]
    [Trait("Criterio", "CA-HU14.1")]
    public async Task LaExplicacionDejaSuEntradaDeTrazaCompletaYLaCadenaVerifica()
    {
        // Una explicación servida desde caché también deja su entrada (T3 de S2-06), así que da igual el orden.
        var (lab, queryId) = await Esc03Async();
        var explanation = await ExplainAsync(lab, queryId, AiResponses.Traced);
        var explanationId = explanation.GetProperty("explanation_id").GetGuid();

        var entries = await Platform.GetJsonAsync(lab.Dba, $"/trace?explanationId={explanationId}");

        var entry = Assert.Single(entries.EnumerateArray());
        Assert.Equal(queryId, entry.GetProperty("query_id").GetGuid());
        Assert.NotEqual(0, entry.GetProperty("finding_codes").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("catalog_version").GetString()));
        Assert.Equal("acceptance-double", entry.GetProperty("model").GetString());
        Assert.Equal(AiServiceDouble.PromptVersion, entry.GetProperty("prompt_version").GetString());
        Assert.Matches("^[0-9a-f]{64}$", entry.GetProperty("context_hash").GetString()!);
        Assert.Equal("TRAZADA", entry.GetProperty("validation_status").GetString());
        Assert.Equal(JsonValueKind.String, entry.GetProperty("recorded_at").ValueKind);

        var verify = await Platform.GetJsonAsync(lab.Dba, "/trace/verify");
        Assert.True(verify.GetProperty("intact").GetBoolean(), $"Cadena de traza: {verify}");
    }

    private async Task<(LabConnection Lab, Guid QueryId)> Esc03Async()
    {
        var lab = await platform.QdLabAsync();
        return (lab, await AnalysisAcceptanceTests.QueryWithRuleAsync(lab, "REG-02", "Region"));
    }

    /// <summary>
    /// Pide la explicación por la Api y espera su estado final en <c>GET …/explanations/latest</c>.
    /// <paramref name="allowCache"/> = false exige que la IA haya recibido la llamada: cada prueba que lo pide usa una
    /// consulta que ninguna otra explica (ESC01, ACC01, ACC02), porque el hash del contexto incluye la consulta.
    /// </summary>
    private async Task<JsonElement> ExplainAsync(
        LabConnection lab,
        Guid queryId,
        Func<string, CancellationToken, Task<HttpResponseMessage>> respond,
        bool allowCache = true)
    {
        platform.Ai.Explain = respond;
        var sent = platform.Ai.ExplainBodies.Count;

        using var response = await lab.Dba.PostAsync($"/queries/{queryId}/explanations", null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var explanationId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("explanation_id").GetGuid();

        var latest = await Platform.UntilAsync(
            () => Platform.GetJsonAsync(lab.Dba, $"/queries/{queryId}/explanations/latest"),
            e => e.GetProperty("explanation_id").GetGuid() == explanationId
                && e.GetProperty("status").GetString() is not ("PENDIENTE" or "EN_CURSO"),
            $"la explicación {explanationId}");

        if (!allowCache && latest.GetProperty("from_cache").GetBoolean())
        {
            Assert.Fail($"La prueba necesitaba una llamada real al doble y salió de caché (enviados antes: {sent}).");
        }

        return latest;
    }
}
