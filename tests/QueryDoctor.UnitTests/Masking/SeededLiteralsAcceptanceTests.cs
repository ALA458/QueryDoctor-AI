using System.Text.Json;
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using static QueryDoctor.UnitTests.Masking.MaskingSentinels;

namespace QueryDoctor.UnitTests.Masking;

/// <summary>
/// "Hecho cuando" de S2-04 y CA-HU21.1: con literales sembrados, las solicitudes registradas hacia la IA
/// contienen marcadores y 0 ocurrencias de esos literales. Las solicitudes se registran con un doble del
/// cliente de IA que captura el cuerpo exacto que se enviaría.
/// </summary>
public sealed class SeededLiteralsAcceptanceTests
{
    private static readonly Guid QueryId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid CorrelationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private readonly LiteralMasker _masker = new();
    private readonly CapturingAiExplanationClient _client = new();

    /// <summary>Lote con todos los casos sembrados de SQL de la suite, separados por GO.</summary>
    private static string SeededBatches() =>
        string.Join("\r\nGO\r\n", LiteralMaskerSqlTests.SeededSql.Select(row => (string)row[1]));

    private async Task GenerateExplanationAsync(string? sql, string? plan, IReadOnlyList<FindingContract> findings)
    {
        var context = _masker.Mask(new MaskingInput(sql, plan, findings));
        var request = MaskedExplanationRequest.TryCreate(QueryId, context, CorrelationId);
        if (request is not null)
        {
            await _client.SendAsync(request, CancellationToken.None);
        }
    }

    [Fact]
    public async Task LaSolicitudRegistradaConSqlPlanYEvidenciaSembradosTieneMarcadoresYCeroCentinelas()
    {
        var findings = LiteralMaskerEvidenceTests.AnalyzeSeeded("ESC01.actual.sqlplan", out var plan);
        Assert.NotEmpty(findings);

        await GenerateExplanationAsync(SeededBatches(), plan, findings);

        var body = Assert.Single(_client.RecordedBodies);
        AssertRecordedBodyIsClean(body);
        Assert.Contains("@qd_str_", body, StringComparison.Ordinal);
        Assert.Contains("@qd_num_", body, StringComparison.Ordinal);
        Assert.Contains("@qd_bin_", body, StringComparison.Ordinal);
        Assert.Contains("@qd_date_", body, StringComparison.Ordinal);
        Assert.Contains("@qd_guid_", body, StringComparison.Ordinal);

        using var json = JsonDocument.Parse(body);
        Assert.Equal(QueryId.ToString(), json.RootElement.GetProperty("query_id").GetString());
        Assert.NotEqual(string.Empty, json.RootElement.GetProperty("sql_text").GetString());
        Assert.Equal(findings.Count, json.RootElement.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public async Task CadaPlanDelLaboratorioConSuSqlSembradoProduceSolicitudesSinCentinelas()
    {
        foreach (var path in MaskingFixtures.FixturePaths())
        {
            var findings = LiteralMaskerEvidenceTests.AnalyzeSeeded(Path.GetFileName(path), out var plan);
            await GenerateExplanationAsync(MaskingFixtures.SeededStatementText, plan, findings);
        }

        Assert.NotEmpty(_client.RecordedBodies);
        Assert.All(_client.RecordedBodies, AssertRecordedBodyIsClean);
    }

    [Fact]
    public async Task SiScriptDomNoTokenizaElSqlSeEnvianSoloLosHallazgosSinTextoSql()
    {
        // RA-03 e invariante 5: el SQL queda fuera, los hallazgos enmascarados sí viajan.
        var findings = LiteralMaskerEvidenceTests.AnalyzeSeeded("ESC01.actual.sqlplan", out var plan);

        await GenerateExplanationAsync($"SELECT * FROM t WHERE c = '{Secret} /* {CommentSecret}", plan, findings);

        var body = Assert.Single(_client.RecordedBodies);
        AssertRecordedBodyIsClean(body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(string.Empty, json.RootElement.GetProperty("sql_text").GetString());
        Assert.Equal(findings.Count, json.RootElement.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public async Task SiNadaEsEnviableElClienteNoRecibeNada()
    {
        // SQL que no tokeniza, plan truncado y el único hallazgo con evidencia sin clasificar.
        var unclassified = LiteralMaskerEvidenceTests.Contract(
            "REG-99",
            new Dictionary<string, string?> { ["clave_desconocida"] = Secret });
        var plan = MaskingFixtures.Seed(MaskingFixtures.Read("ESC03.actual.sqlplan"));

        await GenerateExplanationAsync($"SELECT N'{UnicodeSecret}", plan[..(plan.Length / 3)], [unclassified]);

        Assert.Empty(_client.RecordedBodies);
    }

    [Fact]
    public async Task SinHallazgosNoSeEnviaNadaAunqueElSqlSeaValido()
    {
        await GenerateExplanationAsync($"SELECT '{Secret}'", null, []);

        Assert.Empty(_client.RecordedBodies);
    }

    [Fact]
    public async Task LaMismaEntradaProduceLaMismaSolicitudByteAByte()
    {
        var findings = LiteralMaskerEvidenceTests.AnalyzeSeeded("ESC03.actual.sqlplan", out var plan);

        await GenerateExplanationAsync(SeededBatches(), plan, findings);
        await GenerateExplanationAsync(SeededBatches(), plan, findings.Reverse().ToArray());

        Assert.Equal(2, _client.RecordedBodies.Count);
        Assert.Equal(_client.RecordedBodies[0], _client.RecordedBodies[1]);
    }

    private static void AssertRecordedBodyIsClean(string body)
    {
        AssertNoneIn(body);

        // También sobre los valores ya decodificados del JSON, por si algún escape ocultara un centinela.
        using var json = JsonDocument.Parse(body);
        foreach (var value in StringValues(json.RootElement))
        {
            AssertNoneIn(value);
        }
    }

    private static IEnumerable<string> StringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (var nested in StringValues(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in StringValues(item))
                    {
                        yield return nested;
                    }
                }

                break;
            default:
                break;
        }
    }
}

/// <summary>
/// Doble del cliente de IA: registra el cuerpo exacto que se enviaría a <c>POST /explain</c>.
/// </summary>
internal sealed class CapturingAiExplanationClient : IAiExplanationClient
{
    private readonly List<string> _bodies = [];

    public IReadOnlyList<string> RecordedBodies => _bodies;

    public Task<string> SendAsync(MaskedExplanationRequest request, CancellationToken cancellationToken)
    {
        _bodies.Add(request.ToWireJson());
        return Task.FromResult("{}");
    }
}
