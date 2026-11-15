using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Analysis;

namespace QueryDoctor.UnitTests.Validation;

public sealed class NumericEvidenceMatcherTests
{
    private readonly NumericEvidenceMatcher _matcher = new();

    [Fact]
    public void ExtractMetricTokensParsesThousandsDecimalsAndPercentagesAcrossFormats()
    {
        var text = "El scan leyó 250,000 filas (10 000 por lote) con impacto de 84,5 % en 120ms y ratio 10.5x (-2.5 variación).";

        var tokens = NumericEvidenceMatcher.ExtractMetricTokens(text);

        Assert.Equal([250000d, 10000d, 84.5d, 120d, 10.5d, -2.5d], tokens.Select(static t => t.Value).ToArray());
        Assert.Equal([false, false, true, false, false, false], tokens.Select(static t => t.IsPercentage).ToArray());
    }

    [Fact]
    public void ExtractMetricTokensDoesNotTreatThreeDecimalDigitsAsThousands()
    {
        var tokens = NumericEvidenceMatcher.ExtractMetricTokens("El costo relativo fue 0.854 y la latencia promedio 84.567 ms.");

        Assert.Equal([0.854d, 84.567d], tokens.Select(static t => t.Value).ToArray());
    }

    [Fact]
    public void ExtractMetricTokensExcludesMaskerTokensRulesGuidsSqlTypesVersionsAndAllowedNodes()
    {
        var findingId = "F-A1B2C3D4E5F6";
        var fragmentId = "a1010001-0001-0000-0000-000000000001";
        var text =
            $"En el hallazgo {findingId} (REG-01, {fragmentId}, nodo 4, Col2, SQL2022) " +
            "sobre nvarchar(50) con @qd_num_1, @qd_str_2, @qd_bin_3, @qd_date_4 y @qd_guid_5 " +
            "se estimaron 250,000 filas bajo la versión 16.0.4150.1.";

        var tokens = NumericEvidenceMatcher.ExtractMetricTokens(
            text,
            allowedNodeIds: [4],
            knownFindingIds: [findingId],
            engineVersion: "16.0.4150.1");

        var single = Assert.Single(tokens);
        Assert.Equal(250000d, single.Value);
    }

    [Fact]
    public void ExtractMetricTokensCatchesInventedNodeNumberNotInAllowedNodes()
    {
        var tokens = NumericEvidenceMatcher.ExtractMetricTokens(
            "El problema ocurre en el nodo 99 con 250000 filas.",
            allowedNodeIds: [4]);

        Assert.Equal([99d, 250000d], tokens.Select(static t => t.Value).ToArray());
    }

    [Theory]
    [InlineData("84.6%", 84.567, true)]   // redondeo válido a 1 decimal
    [InlineData("85%", 84.6, true)]      // redondeo válido al entero más cercano
    [InlineData("85.4%", 0.854, true)]   // equivalencia porcentaje ↔ tasa en [0, 1]
    [InlineData("10040", 10000.0, true)] // 0.4% < 0.5% tolerancia relativa
    [InlineData("86%", 84.567, false)]   // fuera de tolerancia
    [InlineData("10100", 10000.0, false)] // 1.0% > 0.5% tolerancia relativa
    [InlineData("13", 12.0, false)]      // entero alterado
    [InlineData("8540%", 85.4, false)]   // no escala valores > 1 como si fueran tasas
    public void IsSupportedByEvidenceRespectsDocumentedRoundingTolerance(
        string literalInClaim,
        double evidenceValue,
        bool expectedMatch)
    {
        var token = Assert.Single(NumericEvidenceMatcher.ExtractMetricTokens(literalInClaim));

        var actualMatch = _matcher.IsSupportedByEvidence(token, [evidenceValue]);

        Assert.Equal(expectedMatch, actualMatch);
    }

    [Fact]
    public void CollectAllowedNumericValuesIgnoresMaskerPlaceholdersInsideEvidence()
    {
        var finding = new ValidatedFindingInput(
            "F-0001",
            "REG-01",
            EvidenceClass.Estimada,
            [3],
            new Dictionary<string, string>
            {
                ["predicate"] = "CONVERT_IMPLICIT(nvarchar(50), [Orders].[Code], @qd_num_1)=@qd_str_2",
                ["estimated_rows"] = "150000",
            });

        var values = NumericEvidenceMatcher.CollectAllowedNumericValues([finding], new Dictionary<string, double> { ["avg_cpu_ms"] = 42.5 });

        Assert.Equal([150000d, 42.5d], values);
    }
}
