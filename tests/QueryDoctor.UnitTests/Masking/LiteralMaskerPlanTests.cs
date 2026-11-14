using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Masking;
using static QueryDoctor.UnitTests.Masking.MaskingSentinels;

namespace QueryDoctor.UnitTests.Masking;

/// <summary>
/// Enmascarado del Showplan XML (S2-04, RNF-SEG-05: "valores de parámetros y predicados del plan") sobre los
/// 16 planes reales del laboratorio con centinelas sembrados, y fallo cerrado ante XML inválido.
/// </summary>
public sealed class LiteralMaskerPlanTests
{
    private readonly LiteralMasker _sut = new();

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in MaskingFixtures.FixturePaths())
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Fact]
    public void ElCorpusTieneLos16PlanesDelLaboratorio()
    {
        Assert.Equal(16, MaskingFixtures.FixturePaths().Count);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void CadaPlanSembradoQuedaSinCentinelasYSigueSiendoUnShowplanValido(string fixture)
    {
        var original = MaskingFixtures.Read(fixture);
        var seeded = MaskingFixtures.Seed(original);
        Assert.Contains(CommentSecret, seeded, StringComparison.Ordinal);
        Assert.Contains(BigNumber, seeded, StringComparison.Ordinal);

        var result = _sut.Mask(new MaskingInput(SqlText: null, PlanXml: seeded));

        Assert.True(result.Plan.IsSendable, $"{fixture}: {result.Plan.FailureCause}");
        AssertNoneIn(result.Plan.Text);
        Assert.DoesNotContain("<!--", result.Plan.Text, StringComparison.Ordinal);

        var reparsedOriginal = new ShowplanXmlParser().Parse(original);
        var reparsedMasked = new ShowplanXmlParser().Parse(result.Plan.Text);
        Assert.True(reparsedMasked.IsSuccess, $"{fixture}: el plan enmascarado ya no se puede analizar.");
        Assert.Equal(
            reparsedOriginal.Plan!.EnumerateAllOperators().Count(),
            reparsedMasked.Plan!.EnumerateAllOperators().Count());
    }

    [Fact]
    public void LosValoresDeParametrosYConstantesQuedanComoMarcadoresTipados()
    {
        var seeded = MaskingFixtures.Seed(MaskingFixtures.Read("ESC03.actual.sqlplan"));

        var result = _sut.Mask(new MaskingInput(null, seeded));

        Assert.Contains("ParameterCompiledValue=\"@qd_str_", result.Plan.Text, StringComparison.Ordinal);
        Assert.Contains("ParameterRuntimeValue=\"@qd_str_", result.Plan.Text, StringComparison.Ordinal);
        Assert.Contains("ParameterCompiledValue=\"(@qd_num_", result.Plan.Text, StringComparison.Ordinal);
        Assert.Contains("ConstValue=\"@qd_str_", result.Plan.Text, StringComparison.Ordinal);
        Assert.True(result.Metrics.CommentsRemoved >= 2);
        Assert.True(result.Metrics.DateLiterals >= 1);
        Assert.True(result.Metrics.GuidLiterals >= 1);
        Assert.True(result.Metrics.BinaryLiterals >= 1);
    }

    [Fact]
    public void ElPlanSinSembrarDelLaboratorioNoConservaSusLiteralesReales()
    {
        // ESC02 compila con N'C012345' y ESC03 con (1234): valores reales del laboratorio.
        var esc02 = _sut.Mask(new MaskingInput(null, MaskingFixtures.Read("ESC02.actual.sqlplan")));
        var esc03 = _sut.Mask(new MaskingInput(null, MaskingFixtures.Read("ESC03.actual.sqlplan")));

        Assert.True(esc02.Plan.IsSendable);
        Assert.DoesNotContain("C012345", esc02.Plan.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("C000007", esc02.Plan.Text, StringComparison.Ordinal);
        Assert.True(esc03.Plan.IsSendable);
        Assert.DoesNotContain("(1234)", esc03.Plan.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("(900.00)", esc03.Plan.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlYPlanCompartenNumeracionYElMismoLiteralRecibeElMismoMarcador()
    {
        var plan = MaskingFixtures.Seed(MaskingFixtures.Read("ESC03.actual.sqlplan"));
        var sql = $"SELECT * FROM dbo.T WHERE c = N'{ConstSecret}'";

        var result = _sut.Mask(new MaskingInput(sql, plan));

        Assert.Equal("SELECT * FROM dbo.T WHERE c = @qd_str_1", result.Sql.Text);
        Assert.Contains("ConstValue=\"@qd_str_1\"", result.Plan.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ElMismoPlanDosVecesProduceLaMismaSalida()
    {
        var seeded = MaskingFixtures.Seed(MaskingFixtures.Read("ESC06.actual.sqlplan"));

        var first = _sut.Mask(new MaskingInput("SELECT 1", seeded));
        var second = new LiteralMasker().Mask(new MaskingInput("SELECT 1", seeded));

        Assert.Equal(first.Plan.Text, second.Plan.Text);
        Assert.Equal(first.Metrics, second.Metrics);
    }

    public static TheoryData<string, string, MaskingFailureCause> InvalidPlans()
    {
        var seeded = MaskingFixtures.Seed(MaskingFixtures.Read("ESC01.actual.sqlplan"));
        const string Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        return new TheoryData<string, string, MaskingFailureCause>
        {
            { "truncado a la mitad", seeded[..(seeded.Length / 2)], MaskingFailureCause.PlanMalformed },
            { "truncado al final", seeded[..^5], MaskingFailureCause.PlanMalformed },
            { "no es XML", $"QD_SECRET_7f3a <ShowPlanXML", MaskingFailureCause.PlanMalformed },
            { "con DTD", $"<!DOCTYPE ShowPlanXML [<!ENTITY s '{Secret}'>]><ShowPlanXML xmlns=\"{Ns}\">&s;</ShowPlanXML>", MaskingFailureCause.PlanDtdProhibited },
            { "raiz inesperada", $"<Other StatementText=\"SELECT '{Secret}'\" />", MaskingFailureCause.PlanUnexpectedRoot },
            { "atributo no catalogado con apostrofo", $"<ShowPlanXML xmlns=\"{Ns}\"><X Note=\"N'{Secret}'\" /></ShowPlanXML>", MaskingFailureCause.PlanUnexpectedLiteralCarrier },
            { "texto dentro de un elemento", $"<ShowPlanXML xmlns=\"{Ns}\"><X>{Secret}</X></ShowPlanXML>", MaskingFailureCause.PlanUnexpectedLiteralCarrier },
            { "StatementText que no tokeniza", $"<ShowPlanXML xmlns=\"{Ns}\"><StmtSimple StatementText=\"SELECT '{Secret}\" /></ShowPlanXML>", MaskingFailureCause.PlanAttributeLexicalError },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public void ElPlanInvalidoOTruncadoEsNoEnviableSinTexto(string caso, string planXml, MaskingFailureCause expected)
    {
        var result = _sut.Mask(new MaskingInput("SELECT 1", planXml));

        Assert.False(result.Plan.IsSendable, caso);
        Assert.Equal(expected, result.Plan.FailureCause);
        Assert.Null(result.Plan.Text);
        Assert.True(result.Sql.IsSendable);
        Assert.Equal(1, result.Metrics.TotalLiterals);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinPlanEsNoEnviable(string? planXml)
    {
        var result = _sut.Mask(new MaskingInput("SELECT 1", planXml));

        Assert.Equal(MaskingFailureCause.InputMissing, result.Plan.FailureCause);
        Assert.Null(result.Plan.Text);
    }
}
