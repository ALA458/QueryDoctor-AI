using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.UnitTests.Persistence;

/// <summary>
/// Pruebas del modelo de persistencia para el esquema ai: kb_fragment y kb_causal_relation (S1-17, RF-IA-04, RF-IA-03).
/// </summary>
public sealed class KnowledgeBaseModelTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _script;

    public KnowledgeBaseModelTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test", npgsql => npgsql.SetPostgresVersion(16, 0))
            .Options;

        _db = new AppDbContext((DbContextOptions<AppDbContext>)options, SystemAccessScope.Instance);
        _script = _db.Database.GenerateCreateScript().Replace("\"", string.Empty, StringComparison.Ordinal);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("ai.kb_fragment")]
    [InlineData("ai.kb_causal_relation")]
    public void LasTablasDeBaseDeConocimientoExistenEnEsquemaAi(string tabla)
    {
        Assert.Contains($"CREATE TABLE {tabla} (", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void KbCausalRelationTieneClaveUnicaCompuesta()
    {
        Assert.Matches(
            @"CREATE UNIQUE INDEX \S+ ON ai\.kb_causal_relation \(rule_id, fragment_id, relation_type\)",
            _script);
    }

    [Fact]
    public void KbCausalRelationTieneClaveForaneaConBorradoEnCascada()
    {
        Assert.Contains(
            "FOREIGN KEY (fragment_id) REFERENCES ai.kb_fragment (id) ON DELETE CASCADE",
            _script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void KbFragmentTieneIndicePorRuleIdParaBusquedaDeterminista()
    {
        Assert.Matches(@"CREATE INDEX \S+ ON ai\.kb_fragment \(rule_id\)", _script);
    }

    [Theory]
    [InlineData("REG-01")]
    [InlineData("REG-02")]
    [InlineData("REG-03")]
    [InlineData("REG-04")]
    [InlineData("REG-11")]
    public void LosFragmentosInicialesContienenLasReglasRequeridas(string ruleId)
    {
        Assert.Contains(ruleId, _script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a1010003-0003-0000-0000-000000000003")]
    [InlineData("a1010003-0003-0000-0000-000000000004")]
    [InlineData("a1010011-0011-0000-0000-000000000001")]
    [InlineData("a1010011-0011-0000-0000-000000000002")]
    public void LosFragmentosAmpliadosDeS208EstanSembradosEnElModelo(string fragmentId)
    {
        Assert.Contains(fragmentId, _script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LosTiposDeRelacionCausalEstanSembrados()
    {
        Assert.Contains("Interpretacion", _script, StringComparison.Ordinal);
        Assert.Contains("Recomendacion", _script, StringComparison.Ordinal);
    }
}
