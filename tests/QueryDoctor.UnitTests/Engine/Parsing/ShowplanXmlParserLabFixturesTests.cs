using System.Diagnostics;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Plans;

namespace QueryDoctor.UnitTests.Engine.Parsing;

/// <summary>
/// Pruebas del <see cref="ShowplanXmlParser"/> contra los 16 planes reales de SQL Server 2022
/// generados por el laboratorio en <c>tests/fixtures/plans/</c> (S1-07, S1-12, RF-MOT-01, RNF-COM-02, RNF-RND-04).
/// </summary>
public sealed class ShowplanXmlParserLabFixturesTests
{
    private readonly ShowplanXmlParser _sut = new();

    [Fact]
    public void ParseBatchConLos16PlanesDelLaboratorioParseaTodosConExitoYConservaEngineVersion()
    {
        var files = Directory.GetFiles(GetFixturesDirectory(), "*.sqlplan")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(16, files.Length);

        var inputs = files
            .Select(path => new PlanInput(Path.GetFileName(path), File.ReadAllText(path)))
            .ToArray();

        var results = _sut.ParseBatch(inputs);

        Assert.Equal(16, results.Count);
        Assert.All(results, result =>
        {
            Assert.True(result.IsSuccess, $"Falló {result.PlanIdentifier}: {result.Finding?.Message}");
            Assert.Equal(PlanAnalysisStatus.Analyzable, result.Status);
            Assert.Null(result.Finding);
            Assert.NotNull(result.Plan);
            Assert.Equal("1.564", result.Plan.SchemaVersion);
            Assert.Equal("16.0.4315.2", result.Plan.EngineVersion);
            Assert.NotEmpty(result.Plan.Statements);
        });
    }

    [Fact]
    public void ParseEsc01QueryStorePlanExtraePlanAffectingConvertImplicitConversionYKeyLookup()
    {
        var xml = LoadFixture("ESC01.qs.1.sqlplan");

        var result = _sut.Parse(xml, "ESC01.qs.1");

        Assert.True(result.IsSuccess);
        var stmt = Assert.Single(result.Plan!.Statements);
        Assert.Equal("SELECT", stmt.StatementType);
        Assert.Equal(20d, stmt.StatementEstRows);
        Assert.Equal(160, stmt.CardinalityEstimationModelVersion);
        Assert.Equal("0x072A499E107D2EA0", stmt.QueryHash);
        Assert.Equal("0x57B9F00E7B87821F", stmt.QueryPlanHash);

        var warning = Assert.Single(stmt.Warnings);
        Assert.Equal(PlanWarningKind.PlanAffectingConvert, warning.Kind);
        Assert.Equal("Seek Plan", warning.ConvertIssue);
        Assert.Contains("CONVERT_IMPLICIT(nvarchar(20)", warning.Expression, StringComparison.Ordinal);

        Assert.NotNull(stmt.RootOperator);
        Assert.Equal(1, stmt.RootOperator.NodeId);
        Assert.Equal("Nested Loops", stmt.RootOperator.PhysicalOp);
        Assert.Equal(2, stmt.RootOperator.Children.Count);

        var indexScan = stmt.RootOperator.Children[0];
        Assert.Equal(3, indexScan.NodeId);
        Assert.Equal("Index Scan", indexScan.PhysicalOp);
        Assert.False(indexScan.IsLookup);
        Assert.Equal(1_000_000d, indexScan.EstimatedRowsRead);
        Assert.Equal(1_000_000d, indexScan.TableCardinality);
        Assert.NotNull(indexScan.TargetObject);
        Assert.Equal("QD_Lab", indexScan.TargetObject.Database);
        Assert.Equal("dbo", indexScan.TargetObject.Schema);
        Assert.Equal("Orders", indexScan.TargetObject.Table);
        Assert.Equal("IX_Orders_CustomerCode", indexScan.TargetObject.Index);
        Assert.Equal("NonClustered", indexScan.TargetObject.IndexKind);

        var conversion = Assert.Single(indexScan.ImplicitConversions);
        Assert.Equal("nvarchar", conversion.TargetDataType);
        Assert.Equal(40, conversion.Length);
        Assert.NotNull(conversion.Column);
        Assert.Equal("Orders", conversion.Column.Table);
        Assert.Equal("CustomerCode", conversion.Column.Column);

        var keyLookup = stmt.RootOperator.Children[1];
        Assert.Equal(5, keyLookup.NodeId);
        Assert.True(keyLookup.IsLookup);
        Assert.Equal(19d, keyLookup.EstimateRebinds);
        Assert.Equal("PK_Orders", keyLookup.TargetObject?.Index);
        Assert.Equal("Amount", Assert.Single(keyLookup.OutputColumns).Column);

        var parameter = Assert.Single(stmt.Parameters);
        Assert.Equal("@code", parameter.Name);
        Assert.Equal("nvarchar(20)", parameter.DataType);
        Assert.Equal("N'C012345'", parameter.CompiledValue);
    }

    [Fact]
    public void ParseEsc02QueryStorePlanExtraeKeyLookupConColumnasDeSalidaSeekColumnsYParametros()
    {
        var xml = LoadFixture("ESC02.qs.1.sqlplan");

        var result = _sut.Parse(xml, "ESC02.qs.1");

        Assert.True(result.IsSuccess);
        var stmt = Assert.Single(result.Plan!.Statements);
        var root = stmt.RootOperator!;
        Assert.Equal(0, root.NodeId);
        Assert.Equal("Nested Loops", root.PhysicalOp);

        var seek = root.Children[0];
        Assert.Equal(1, seek.NodeId);
        Assert.Equal("Index Seek", seek.PhysicalOp);
        Assert.False(seek.IsLookup);
        Assert.True(seek.IsOrdered);
        Assert.Equal("IX_Orders_CustomerId", seek.TargetObject?.Index);
        Assert.Equal("CustomerId", Assert.Single(seek.SeekColumns).Column);

        var lookup = root.Children[1];
        Assert.Equal(3, lookup.NodeId);
        Assert.True(lookup.IsLookup);
        Assert.Equal(19d, lookup.EstimateRebinds);
        Assert.Equal(["Amount", "CreatedAt"], lookup.OutputColumns.Select(c => c.Column).ToArray());
        Assert.Equal("OrderId", Assert.Single(lookup.SeekColumns).Column);

        var param = Assert.Single(stmt.Parameters);
        Assert.Equal("@id", param.Name);
        Assert.Equal("int", param.DataType);
        Assert.Equal("(1234)", param.CompiledValue);
    }

    [Fact]
    public void ParseEsc03QueryStorePlanExtraeMissingIndexSugeridoYClusteredIndexScanConPredicado()
    {
        var xml = LoadFixture("ESC03.qs.1.sqlplan");

        var result = _sut.Parse(xml, "ESC03.qs.1");

        Assert.True(result.IsSuccess);
        var stmt = Assert.Single(result.Plan!.Statements);

        var group = Assert.Single(stmt.MissingIndexGroups);
        Assert.Equal(99.277, group.Impact, precision: 3);

        var suggestion = Assert.Single(group.Indexes);
        Assert.Equal("QD_Lab", suggestion.Database);
        Assert.Equal("dbo", suggestion.Schema);
        Assert.Equal("Orders", suggestion.Table);
        Assert.Equal(["Region"], suggestion.EqualityColumns);
        Assert.Equal(["Amount"], suggestion.InequalityColumns);
        Assert.Empty(suggestion.IncludeColumns);

        var root = stmt.RootOperator!;
        Assert.Equal("Parallelism", root.PhysicalOp);
        Assert.True(root.IsParallel);

        var scan = Assert.Single(root.Children);
        Assert.Equal("Clustered Index Scan", scan.PhysicalOp);
        Assert.Equal(16172.1, scan.EstimateRows, precision: 1);
        Assert.Equal(1_000_000d, scan.EstimatedRowsRead);
        Assert.Contains("[Region]=[@region]", scan.PredicateScalarString, StringComparison.Ordinal);
        Assert.Contains("(900.00)", scan.ConstantLiterals);
    }

    [Fact]
    public void ParseEsc04DispatcherPlanEsAnalizableSinOperadoresRelOp()
    {
        var xml = LoadFixture("ESC04.qs.1.sqlplan");

        var result = _sut.Parse(xml, "ESC04.qs.1");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Finding);
        var stmt = Assert.Single(result.Plan!.Statements);
        Assert.Equal("MULTIPLE PLAN", stmt.StatementType);
        Assert.True(stmt.IsDispatcherPlan);
        Assert.Null(stmt.RootOperator);
        Assert.Empty(result.Plan.EnumerateAllOperators());
    }

    [Fact]
    public void ParseEsc05ActualVsQueryStoreIgnoraRunTimeInformationYExtraeSpillToTempDbEnPlanReal()
    {
        var qsResult = _sut.Parse(LoadFixture("ESC05.qs.1.sqlplan"), "ESC05.qs.1");
        var actualResult = _sut.Parse(LoadFixture("ESC05.actual.sqlplan"), "ESC05.actual");

        Assert.True(qsResult.IsSuccess);
        Assert.True(actualResult.IsSuccess);

        var qsSort = qsResult.Plan!.EnumerateAllOperators().Single(op => op.PhysicalOp == "Sort");
        Assert.Empty(qsSort.Warnings);

        var actualSort = actualResult.Plan!.EnumerateAllOperators().Single(op => op.PhysicalOp == "Sort");
        Assert.Equal(2, actualSort.Warnings.Count);
        var spill = Assert.Single(actualSort.Warnings, w => w.Kind == PlanWarningKind.SpillToTempDb);
        Assert.Equal(2, spill.SpillLevel);
        Assert.Equal(12, spill.SpilledThreadCount);
        Assert.Contains(actualSort.Warnings, w => w.Kind == PlanWarningKind.SortSpillDetails);

        var actualGrant = actualResult.Plan.Statements[0].MemoryGrant;
        Assert.NotNull(actualGrant);
        Assert.Equal(7296L, actualGrant.GrantedMemoryKb);
    }

    [Fact]
    public void ParseEsc06PlanProfundamenteAnidadoF8Parsea140NivelesEnMenosDe500Ms()
    {
        var xml = LoadFixture("ESC06.qs.1.sqlplan");
        var sw = Stopwatch.StartNew();

        var result = _sut.Parse(xml, "ESC06.qs.1");
        sw.Stop();

        Assert.True(result.IsSuccess);
        var operators = result.Plan!.EnumerateAllOperators().ToList();
        Assert.Equal(279, operators.Count);
        Assert.True(
            sw.ElapsedMilliseconds < 500,
            $"El parseo de ESC06 (304 KB, 140 niveles) tardó {sw.ElapsedMilliseconds} ms y debe ser < 500 ms (RNF-RND-04).");
    }

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(GetFixturesDirectory(), fileName));

    private static string GetFixturesDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "fixtures", "plans");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró el directorio 'tests/fixtures/plans'.");
    }
}
