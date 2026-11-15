using Microsoft.EntityFrameworkCore;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.UnitTests.Persistence;

/// <summary>
/// Modelo de <c>ai.explanation</c> (S2-05, E4): cuerpo cifrado en texto, hash para la caché (RF-IA-07) y
/// borrado en cascada con la consulta (retención §7).
/// </summary>
public sealed class ExplanationModelTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _script;

    public ExplanationModelTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test", npgsql => npgsql.SetPostgresVersion(16, 0))
            .Options;

        _db = new AppDbContext((DbContextOptions<AppDbContext>)options, SystemAccessScope.Instance);
        _script = _db.Database.GenerateCreateScript().Replace("\"", string.Empty, StringComparison.Ordinal);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void LaTablaExisteEnElEsquemaAiConCuerpoCifradoComoTexto()
    {
        Assert.Contains("CREATE TABLE ai.explanation (", _script, StringComparison.Ordinal);
        Assert.Contains("encrypted_body text", _script, StringComparison.Ordinal);
        Assert.Contains("context_hash character varying(64)", _script, StringComparison.Ordinal);
        Assert.Contains("state character varying(32) NOT NULL", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void ElHashYLaConsultaTienenIndice()
    {
        Assert.Matches(@"CREATE INDEX \S+ ON ai\.explanation \(context_hash\)", _script);
        Assert.Matches(@"CREATE INDEX \S+ ON ai\.explanation \(query_id, requested_at\)", _script);
    }

    [Fact]
    public void LasAfirmacionesYSusReferenciasTienenTablaPropiaYSiguenASuExplicacion()
    {
        // §7: ai.claim con texto cifrado y ai.claim_reference con hallazgo y nodos (RF-DASH-05).
        Assert.Contains("CREATE TABLE ai.claim (", _script, StringComparison.Ordinal);
        Assert.Contains("encrypted_text text NOT NULL", _script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE ai.claim_reference (", _script, StringComparison.Ordinal);
        Assert.Contains("node_ids_json text NOT NULL", _script, StringComparison.Ordinal);
        Assert.Matches(@"CREATE UNIQUE INDEX \S+ ON ai\.claim \(explanation_id, claim_index\)", _script);
        Assert.Contains(
            "FOREIGN KEY (explanation_id) REFERENCES ai.explanation (id) ON DELETE CASCADE",
            _script,
            StringComparison.Ordinal);
        Assert.Contains(
            "FOREIGN KEY (claim_id) REFERENCES ai.claim (id) ON DELETE CASCADE",
            _script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LaExplicacionSigueASuConsultaConBorradoEnCascada()
    {
        Assert.Contains(
            "FOREIGN KEY (query_id) REFERENCES core.query (id) ON DELETE CASCADE",
            _script,
            StringComparison.Ordinal);
    }
}
