using QueryDoctor.Application.Ingestion;
using QueryDoctor.Domain.Core;
using static QueryDoctor.UnitTests.Ingestion.RuntimeStatAggregatorTests;

namespace QueryDoctor.UnitTests.Ingestion;

// RF-ING-05 / RNF-FIA-01: la marca de agua solo consolida intervalos cerrados y nunca retrocede.
public class WatermarkCalculatorTests
{
    [Fact]
    public void AvanzaAlMayorIntervaloCerrado()
    {
        var rows = new[] { Row(1, 10, Metrics(1, 1d)), Row(1, 12, Metrics(1, 1d)), Row(2, 11, Metrics(1, 1d)) };

        Assert.Equal(12, WatermarkCalculator.Next(9, rows));
    }

    [Fact]
    public void ElIntervaloActivoNoConsolida()
    {
        var rows = new[] { Row(1, 10, Metrics(1, 1d)), Row(1, 11, Metrics(1, 1d), closed: false) };

        Assert.Equal(10, WatermarkCalculator.Next(9, rows));
    }

    [Fact]
    public void SinIntervalosCerradosSeConservaLaMarca()
    {
        Assert.Equal(9, WatermarkCalculator.Next(9, [Row(1, 10, Metrics(1, 1d), closed: false)]));
        Assert.Equal(9, WatermarkCalculator.Next(9, []));
        Assert.Null(WatermarkCalculator.Next(null, []));
    }

    [Fact]
    public void NuncaRetrocede()
    {
        Assert.Equal(20, WatermarkCalculator.Next(20, [Row(1, 5, Metrics(1, 1d))]));
    }

    [Fact]
    public void LaPrimeraIngestaLeeDesdeCero()
    {
        Assert.Equal(0, WatermarkCalculator.AfterIntervalId(null));
        Assert.Equal(42, WatermarkCalculator.AfterIntervalId(42));
    }

    [Theory]
    [InlineData(0, ExecutionType.Regular)]
    [InlineData(3, ExecutionType.Aborted)]
    [InlineData(4, ExecutionType.Exception)]
    [InlineData(1, null)]
    [InlineData(99, null)]
    public void TiposDeEjecucionDeQueryStore(int code, ExecutionType? expected)
    {
        Assert.Equal(expected, QueryStoreExecutionType.FromCode(code));
    }
}

// RF-ING-09 / CA-HU03.3: un plan nulo, truncado o inválido queda no analizable con su causa; nunca lanza.
public class PlanXmlClassifierTests
{
    private const string Minimal =
        "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\" Version=\"1.564\"><BatchSequence/></ShowPlanXML>";

    [Fact]
    public void UnShowplanBienFormadoQuedaPendienteDeAnalisis()
    {
        Assert.Equal(PlanClassification.Pending, PlanXmlClassifier.Classify(true, Minimal));
    }

    [Theory]
    [InlineData("ESC01.qs.1.sqlplan")]
    [InlineData("ESC04.qs.1.sqlplan")]
    [InlineData("ESC06.qs.1.sqlplan")]
    public void LosPlanesDelLaboratorioSonAnalizables(string fixture)
    {
        // ESC04.qs.1 es un Dispatcher de PSP sin RelOp y ESC06 tiene 140 niveles (F8): ambos son XML válidos.
        var xml = File.ReadAllText(FixturePath(fixture));

        Assert.Equal(PlanClassification.Pending, PlanXmlClassifier.Classify(true, xml));
    }

    public static TheoryData<bool, string?, PlanNotAnalyzableCause> NotAnalyzableCases => new()
    {
        { false, null, PlanNotAnalyzableCause.PlanNotFound },
        { true, null, PlanNotAnalyzableCause.NullXml },
        { true, "", PlanNotAnalyzableCause.EmptyXml },
        { true, "   \r\n", PlanNotAnalyzableCause.EmptyXml },
        { true, Minimal[..60], PlanNotAnalyzableCause.MalformedXml },
        { true, "no es xml", PlanNotAnalyzableCause.MalformedXml },
        { true, "<ShowPlanXML><a></ShowPlanXML>", PlanNotAnalyzableCause.MalformedXml },
        { true, "<!DOCTYPE x [<!ENTITY e \"boom\">]><ShowPlanXML>&e;</ShowPlanXML>", PlanNotAnalyzableCause.MalformedXml },
        { true, "<Otro/>", PlanNotAnalyzableCause.NotShowplan },
    };

    [Theory]
    [MemberData(nameof(NotAnalyzableCases))]
    public void LosPlanesIrrecuperablesQuedanNoAnalizablesConCausa(bool found, string? xml, PlanNotAnalyzableCause cause)
    {
        var result = PlanXmlClassifier.Classify(found, xml);

        Assert.Equal(PlanAnalysisStatus.NotAnalyzable, result.Status);
        Assert.Equal(cause, result.Cause);
    }

    [Fact]
    public void UnPlanDemasiadoGrandeNoSeProcesa()
    {
        Assert.Equal(PlanNotAnalyzableCause.TooLarge, PlanXmlClassifier.Classify(true, Minimal, maxCharacters: 10).Cause);
    }

    private static string FixturePath(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures", "plans", name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(name);
    }
}
