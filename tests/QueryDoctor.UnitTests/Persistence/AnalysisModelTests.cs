using Microsoft.EntityFrameworkCore;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.UnitTests.Persistence;

/// <summary>
/// S1-13: verifica el esquema SQL generado por EF Core para el esquema analysis (rule_catalog, threshold_set, analysis_run, finding).
/// </summary>
public sealed class AnalysisModelTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _script;

    public AnalysisModelTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test", npgsql => npgsql.SetPostgresVersion(16, 0))
            .Options;

        _db = new AppDbContext((DbContextOptions<AppDbContext>)options);
        _script = _db.Database.GenerateCreateScript().Replace("\"", string.Empty, StringComparison.Ordinal);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("analysis.rule_catalog")]
    [InlineData("analysis.threshold_set")]
    [InlineData("analysis.analysis_run")]
    [InlineData("analysis.finding")]
    public void LasTablasDelEsquemaAnalysisExisten(string tabla)
    {
        Assert.Contains($"CREATE TABLE {tabla} (", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void RuleCatalogYThresholdSetTienenClavesNaturalesUnicas()
    {
        Assert.Matches(
            @"CREATE UNIQUE INDEX \S+ ON analysis\.rule_catalog \(rule_id, catalog_version, rule_version\)",
            _script);

        Assert.Matches(
            @"CREATE UNIQUE INDEX \S+ ON analysis\.threshold_set \(catalog_version, configuration_hash\)",
            _script);
    }

    [Fact]
    public void FindingTieneClaveUnicaPorCorridaYFindingCodeDeterminista()
    {
        Assert.Matches(
            @"CREATE UNIQUE INDEX \S+ ON analysis\.finding \(analysis_run_id, finding_code\)",
            _script);
    }

    [Fact]
    public void FindingGuardaEvidenciaCifradaEnReposo()
    {
        // RNF-SEG-14 y Arquitectura §7: los campos de evidencia que pueden contener literales van cifrados.
        Assert.Contains("encrypted_evidence_json", _script, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bevidence_json\b", _script);
    }

    [Theory]
    [InlineData("REG-01")]
    [InlineData("REG-02")]
    [InlineData("REG-03")]
    [InlineData("REG-04")]
    [InlineData("REG-05")]
    [InlineData("REG-06")]
    [InlineData("REG-07")]
    [InlineData("REG-08")]
    [InlineData("REG-09")]
    [InlineData("REG-10")]
    [InlineData("REG-11")]
    public void LasOnceReglasDelCatalogoEstanSembradas(string ruleId)
    {
        Assert.Contains($"'{ruleId}'", _script, StringComparison.Ordinal);
    }
}
