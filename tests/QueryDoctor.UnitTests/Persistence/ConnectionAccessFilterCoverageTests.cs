using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Domain.Audit;
using QueryDoctor.Domain.Core;
using QueryDoctor.Domain.Ops;
using QueryDoctor.Infrastructure.Persistence;

namespace QueryDoctor.UnitTests.Persistence;

/// <summary>
/// S2-12 (RNF-SEG-12): toda entidad con datos de una conexión lleva el filtro global por conexión asignada. Recorre el
/// modelo real de EF: una entidad nueva con <c>ConnectionId</c>, o hija de una filtrada, sin filtro deja esta prueba en
/// rojo. Las excepciones están escritas aquí con su motivo.
/// </summary>
public sealed class ConnectionAccessFilterCoverageTests : IDisposable
{
    /// <summary>Entidades con connection_id que, a propósito, no llevan el filtro.</summary>
    private static readonly Dictionary<Type, string> Exempt = new()
    {
        [typeof(UserConnection)] = "Es la fuente del propio scope (IAssignedConnectionsReader) y solo la gestiona el DBA.",
        [typeof(TraceEntry)] = "ChainWriter lee la última seq de toda la cadena, también desde la Api; solo el DBA la consulta.",
    };

    private readonly AppDbContext _db;

    public ConnectionAccessFilterCoverageTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test", npgsql => npgsql.SetPostgresVersion(16, 0))
            .Options;
        _db = new AppDbContext(options, SystemAccessScope.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void TodaEntidadConDatosDeUnaConexionTieneElFiltro()
    {
        var missing = _db.Model.GetEntityTypes()
            .Where(RequiresFilter)
            .Where(e => !Exempt.ContainsKey(e.ClrType))
            .Where(e => !HasConnectionFilter(e))
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(missing.Count == 0, $"Sin filtro por conexión: {string.Join(", ", missing)}");
    }

    [Fact]
    public void LasExcepcionesSiguenSiendoNecesariasYNoTienenFiltro()
    {
        // Si una excepción deja de tener connection_id o gana un filtro, la lista quedó vieja: se corrige aquí.
        foreach (var type in Exempt.Keys)
        {
            var entity = _db.Model.FindEntityType(type);
            Assert.NotNull(entity);
            Assert.True(RequiresFilter(entity), $"{type.Name} ya no tiene datos de una conexión: quítala de las excepciones.");
            Assert.False(HasConnectionFilter(entity), $"{type.Name} tiene el filtro: quítala de las excepciones.");
        }
    }

    [Fact]
    public void LasEntidadesConocidasEstanCubiertas()
    {
        // Ancla: si la detección por modelo fallara en silencio, esta lista explícita lo delata.
        string[] expected =
        [
            "Connection", "CapabilityProfile", "QueryText", "Query", "Plan", "RuntimeStat", "IngestionRun",
            "AnalysisRun", "Finding", "HealthScore", "Explanation", "ExplanationClaim", "ExplanationClaimReference",
        ];

        var filtered = _db.Model.GetEntityTypes().Where(HasConnectionFilter).Select(e => e.ClrType.Name).ToHashSet();

        Assert.All(expected, name => Assert.Contains(name, filtered));
    }

    private static bool HasConnectionFilter(IReadOnlyEntityType entity) =>
        entity.FindDeclaredQueryFilter(AppDbContext.ConnectionAccessFilter) is not null;

    // Datos de una conexión: la propia Connection, una entidad con ConnectionId o una hija (por FK) de una de ellas.
    private static bool RequiresFilter(IReadOnlyEntityType entity) => RequiresFilter(entity, []);

    private static bool RequiresFilter(IReadOnlyEntityType entity, HashSet<IReadOnlyEntityType> visiting)
    {
        if (entity.ClrType == typeof(Connection) || entity.FindProperty(nameof(UserConnection.ConnectionId)) is not null)
        {
            return true;
        }

        if (!visiting.Add(entity))
        {
            return false;
        }

        return entity.GetForeignKeys().Any(fk => RequiresFilter(fk.PrincipalEntityType, visiting));
    }
}
