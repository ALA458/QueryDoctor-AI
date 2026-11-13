using Microsoft.EntityFrameworkCore;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.UnitTests.Persistence;

// S1-04: comprueba el esquema que EF Core generaría, sin necesitar una base de datos.
// La prueba contra PostgreSQL real (duplicados rechazados) se hace a mano con docker compose.
public sealed class CoreModelTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _script;

    public CoreModelTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test", npgsql => npgsql.SetPostgresVersion(16, 0))
            .Options;

        _db = new AppDbContext((DbContextOptions<AppDbContext>)options);

        // Quitamos las comillas para que la comparación no dependa de cómo cita Npgsql los nombres.
        _script = _db.Database.GenerateCreateScript().Replace("\"", string.Empty, StringComparison.Ordinal);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("core.connection")]
    [InlineData("core.capability_profile")]
    [InlineData("core.query_text")]
    [InlineData("core.query")]
    [InlineData("core.plan")]
    [InlineData("core.runtime_stat")]
    [InlineData("core.ingestion_run")]
    [InlineData("ops.user_connection")]
    [InlineData("ops.users")]
    [InlineData("ops.roles")]
    public void LaTablaExisteEnSuEsquema(string tabla)
    {
        Assert.Contains($"CREATE TABLE {tabla} (", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeStatTieneClaveNaturalUnica()
    {
        Assert.Matches(
            @"CREATE UNIQUE INDEX \S+ ON core\.runtime_stat \(connection_id, plan_id, execution_type, interval_id\)",
            _script);
    }

    [Fact]
    public void ConnectionNoPermiteRegistrarDosVecesLaMismaBase()
    {
        // S1-09: la unicidad no distingue mayúsculas en la base (columna generada lower(database_name)).
        Assert.Matches(@"CREATE UNIQUE INDEX \S+ ON core\.connection \(server, database_name_key\)", _script);
        Assert.Matches(@"database_name_key character varying\(128\) GENERATED ALWAYS AS \(lower\(database_name\)\) STORED", _script);
    }

    [Fact]
    public void ConnectionGuardaElCodigoDelFalloDeSondeoNoElMensaje()
    {
        // Punto 3 de la aprobación de S1-09: código corto de un enum cerrado, nunca el texto de la SqlException.
        Assert.Contains("probe_failure_cause character varying(32)", _script, StringComparison.Ordinal);
        Assert.Contains("last_probed_at timestamp with time zone", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void QueryPlanYTextoTienenClaveNaturalUnicaPorConexion()
    {
        Assert.Matches(@"CREATE UNIQUE INDEX \S+ ON core\.query \(connection_id, query_store_query_id\)", _script);
        Assert.Matches(@"CREATE UNIQUE INDEX \S+ ON core\.plan \(connection_id, query_store_plan_id\)", _script);
        Assert.Matches(@"CREATE UNIQUE INDEX \S+ ON core\.query_text \(connection_id, query_store_text_id\)", _script);
    }

    [Fact]
    public void UnaSolaAsignacionPorUsuarioYConexion()
    {
        Assert.Contains("PRIMARY KEY (user_id, connection_id)", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void NingunaColumnaGuardaSqlPlanOCredencialesEnClaro()
    {
        // RNF-SEG-14: los campos sensibles se llaman encrypted_* y los llena quien ya los cifró.
        Assert.DoesNotMatch(@"\b(sql_text|plan_xml|credentials)\b", _script);
        Assert.Contains("encrypted_sql_text", _script, StringComparison.Ordinal);
        Assert.Contains("encrypted_plan_xml", _script, StringComparison.Ordinal);
        Assert.Contains("encrypted_credentials", _script, StringComparison.Ordinal);
    }

    [Fact]
    public void LosDosRolesDeLaPlataformaEstanSembrados()
    {
        Assert.Contains("DBA", _script, StringComparison.Ordinal);
        Assert.Contains("Desarrollador", _script, StringComparison.Ordinal);
    }
}
