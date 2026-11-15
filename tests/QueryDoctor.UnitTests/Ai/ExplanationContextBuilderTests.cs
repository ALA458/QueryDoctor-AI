using System.Text.Json;
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Masking;
using QueryDoctor.Domain.Ai;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.UnitTests.Masking;

namespace QueryDoctor.UnitTests.Ai;

/// <summary>
/// S2-05: contexto de la explicación (RF-IA-01, RF-IA-04), hash canónico para la caché (RF-IA-07) y contrato del
/// cuerpo contra <c>ExplainRequest</c> de S1-18 (deuda H6 de S2-04).
/// </summary>
public sealed class ExplanationContextBuilderTests
{
    private static readonly Guid QueryId = Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020100");

    private static readonly VersionedRuleCatalog Catalog = new(VersionedRuleCatalog.DiscoverBuiltInRules());

    private static ExplanationContextBuilder Builder() => new(new LiteralMasker(), Catalog);

    internal static HealthScore Score() => new()
    {
        Id = Guid.NewGuid(),
        QueryId = QueryId,
        Score = 37.5,
        WeightedConsumptionPct = 42.123456,
        TotalExecutions = 1_000,
        TotalCpuMicroseconds = 2_500_000,
        TotalDurationMicroseconds = 4_000_000,
        TotalLogicalReads = 1_234_000,
    };

    internal static KbFragment Fragment(string ruleId, string content = "Contenido curado") => new()
    {
        Id = Guid.NewGuid(),
        RuleId = ruleId,
        Title = $"Fragmento {ruleId}",
        Content = content,
        SourceCitation = "Microsoft Learn",
    };

    internal static ExplanationSource Source(out IReadOnlyList<FindingContract> findings, string fixture = "ESC01.actual.sqlplan")
    {
        findings = LiteralMaskerEvidenceTests.AnalyzeSeeded(fixture, out var plan);
        return new ExplanationSource(QueryId, MaskingFixtures.SeededStatementText, plan, findings, Score(), "16.0.4315.2");
    }

    [Fact]
    public void ElCuerpoCumpleLosCamposRequeridosDeExplainRequest()
    {
        // Refleja ai-service/src/models/schemas.py (S1-18): ExplainRequest, FindingInput y KbFragmentInput.
        var source = Source(out var findings);
        var kb = findings.Select(f => f.Rule.Id).Distinct().Select(r => Fragment(r)).ToArray();

        var request = Builder().Build(source, kb, Guid.NewGuid()).Request!;
        using var json = JsonDocument.Parse(request.ToWireJson());
        var root = json.RootElement;

        Assert.Equal(JsonValueKind.String, root.GetProperty("query_id").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("sql_text").ValueKind);
        Assert.Equal(JsonValueKind.String, root.GetProperty("correlation_id").ValueKind);
        Assert.Equal(JsonValueKind.Object, root.GetProperty("metrics").ValueKind);
        Assert.True(root.GetProperty("findings").GetArrayLength() >= 1, "ExplainRequest exige min_length=1 (RF-IA-01).");

        foreach (var finding in root.GetProperty("findings").EnumerateArray())
        {
            foreach (var required in new[] { "finding_id", "rule_id", "rule_name", "description" })
            {
                Assert.False(string.IsNullOrWhiteSpace(finding.GetProperty(required).GetString()), required);
            }

            Assert.Equal(JsonValueKind.Array, finding.GetProperty("target_nodes").ValueKind);
            Assert.Equal(JsonValueKind.Object, finding.GetProperty("metrics").ValueKind);
        }

        Assert.NotEqual(0, root.GetProperty("kb_fragments").GetArrayLength());
        foreach (var fragment in root.GetProperty("kb_fragments").EnumerateArray())
        {
            foreach (var required in new[] { "fragment_id", "rule_id", "title", "content", "source_citation" })
            {
                Assert.False(string.IsNullOrWhiteSpace(fragment.GetProperty(required).GetString()), required);
            }

            // ExplanationValidator (S2-03) solo acepta ids de fragmento con formato Guid.
            Assert.True(Guid.TryParse(fragment.GetProperty("fragment_id").GetString(), out _));
        }
    }

    [Fact]
    public void LaDescripcionVieneDelCatalogoYNoDelNombreDeLaRegla()
    {
        var source = Source(out _);

        var request = Builder().Build(source, []).Request!;
        using var json = JsonDocument.Parse(request.ToWireJson());

        foreach (var finding in json.RootElement.GetProperty("findings").EnumerateArray())
        {
            var ruleId = finding.GetProperty("rule_id").GetString()!;
            Assert.Equal(Catalog.GetDescriptor(ruleId).Description, finding.GetProperty("description").GetString());
        }
    }

    [Fact]
    public void SoloViajanFragmentosDeLasReglasConHallazgosEnviables()
    {
        var source = Source(out var findings);
        var rules = findings.Select(f => f.Rule.Id).Distinct().ToArray();
        var ajeno = Fragment("REG-09");

        var request = Builder().Build(source, [.. rules.Select(r => Fragment(r)), ajeno]).Request!;
        using var json = JsonDocument.Parse(request.ToWireJson());

        var sentRules = json.RootElement.GetProperty("kb_fragments").EnumerateArray()
            .Select(k => k.GetProperty("rule_id").GetString()!)
            .ToHashSet();
        Assert.DoesNotContain("REG-09", sentRules);
        Assert.Equal(rules.ToHashSet(), sentRules);
    }

    [Fact]
    public void LasMetricasSonLasDelUltimoPuntajeEnPromedioPorEjecucion()
    {
        var metrics = ExplanationContextBuilder.QueryMetricsFrom(Score());

        Assert.Equal(1_000, metrics["executions"]);
        Assert.Equal(4.0, metrics["avg_duration_ms"]);
        Assert.Equal(2.5, metrics["avg_cpu_ms"]);
        Assert.Equal(1_234, metrics["avg_logical_reads"]);
        Assert.Equal(42.1235, metrics["weighted_consumption_pct"]);
        Assert.Equal(37.5, metrics["health_score"]);
        Assert.Empty(ExplanationContextBuilder.QueryMetricsFrom(null));
    }

    [Fact]
    public void ElHashEsDeterministaIgnoraLaCorrelacionYEsHexadecimalDe64()
    {
        var source = Source(out var findings);
        var kb = findings.Select(f => f.Rule.Id).Distinct().Select(r => Fragment(r)).ToArray();

        var first = Builder().Build(source, kb, Guid.NewGuid()).Request!.ComputeContextHash(ExplainQueryHarness.Profile);
        var second = Builder().Build(source, kb.Reverse().ToArray(), Guid.NewGuid()).Request!.ComputeContextHash(ExplainQueryHarness.Profile);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.All(first, c => Assert.True(char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)));
    }

    [Fact]
    public void ElHashCambiaSiCambiaElContextoOElFragmentoCurado()
    {
        var source = Source(out var findings);
        var rule = findings[0].Rule.Id;
        var fragment = Fragment(rule, "Versión 1");
        var profile = ExplainQueryHarness.Profile;
        var baseline = Builder().Build(source, [fragment]).Request!.ComputeContextHash(profile);

        var changedKb = new KbFragment
        {
            Id = fragment.Id,
            RuleId = rule,
            Title = fragment.Title,
            Content = "Versión 2",
            SourceCitation = fragment.SourceCitation,
        };
        var withChangedKb = Builder().Build(source, [changedKb]).Request!.ComputeContextHash(profile);
        var withOtherMetrics = Builder().Build(source with { LatestScore = null }, [fragment]).Request!.ComputeContextHash(profile);
        var withOtherSql = Builder().Build(source with { SqlText = "SELECT 1" }, [fragment]).Request!.ComputeContextHash(profile);
        var request = Builder().Build(source, [fragment]).Request!;

        Assert.NotEqual(baseline, withChangedKb);
        Assert.NotEqual(baseline, withOtherMetrics);
        Assert.NotEqual(baseline, withOtherSql);

        // RF-IA-07: otro prompt u otro perfil de LLM no reutilizan la explicación.
        Assert.NotEqual(baseline, request.ComputeContextHash(profile with { PromptVersion = "v2" }));
        Assert.NotEqual(baseline, request.ComputeContextHash(profile with { LlmProfile = "openrouter_paid" }));
    }

    [Fact]
    public void SinHallazgosNoHaySolicitud()
    {
        var source = Source(out _) with { Findings = [] };

        var context = Builder().Build(source, []);

        Assert.Null(context.Request);
    }

    [Fact]
    public void ElCuerpoConKbYMetricasSigueSinCentinelas()
    {
        var source = Source(out var findings);
        var kb = findings.Select(f => f.Rule.Id).Distinct().Select(r => Fragment(r)).ToArray();

        var request = Builder().Build(source, kb, Guid.NewGuid()).Request!;

        MaskingSentinels.AssertNoneIn(request.ToWireJson());
    }
}
