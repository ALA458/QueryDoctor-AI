using System.Net.Http;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using QueryDoctor.Infrastructure.Security;
using QueryDoctor.Infrastructure.Telemetry;

namespace QueryDoctor.ArchitectureTests;

/// <summary>
/// Pruebas negativas ("canario") del criterio de aceptación de S1-03:
/// "Las 3 pruebas pasan y cada una falla al romper su regla a propósito".
/// Evalúa las mismas reglas de <see cref="LayerDependencyTests"/> contra tipos
/// sintéticos que violan deliberadamente cada invariante para demostrar que ninguna
/// regla pasa por vacuidad.
/// </summary>
public class ArchitectureRuleCanaryTests
{
    private static readonly Assembly TestAssembly = typeof(ArchitectureRuleCanaryTests).Assembly;

    [Fact]
    public void Regla1FallaCuandoDominioOMotorDependeDeEfCoreOSystemNetHttp()
    {
        var canaryDomainTypes = Types.InAssembly(TestAssembly)
            .That()
            .HaveName(nameof(CanaryDomainEntityWithEfCore));

        var domainResult = LayerDependencyTests.EvaluateDomainPurity(canaryDomainTypes);

        Assert.False(domainResult.IsSuccessful);
        Assert.Contains(
            domainResult.FailingTypeNames ?? [],
            name => name.EndsWith(nameof(CanaryDomainEntityWithEfCore), StringComparison.Ordinal));

        var canaryEngineTypes = Types.InAssembly(TestAssembly)
            .That()
            .HaveName(nameof(CanaryEngineRuleWithHttpAndFileIo));

        var engineResult = LayerDependencyTests.EvaluateDeterministicEnginePurity(canaryEngineTypes);

        Assert.False(engineResult.IsSuccessful);
        Assert.Contains(
            engineResult.FailingTypeNames ?? [],
            name => name.EndsWith(nameof(CanaryEngineRuleWithHttpAndFileIo), StringComparison.Ordinal));
    }

    [Fact]
    public void Regla2FallaCuandoUnModuloAccedeAInternosAjenosOCuandoApiUsaElGateway()
    {
        var canaryCrossModuleTypes = Types.InAssembly(TestAssembly)
            .That()
            .HaveName(nameof(CanaryModuleAccessingForeignInternal));

        var moduleResult = LayerDependencyTests.EvaluateModuleIsolation(
            canaryCrossModuleTypes,
            "QueryDoctor.Infrastructure.Security",
            "QueryDoctor.Infrastructure.Persistence");

        Assert.False(moduleResult.IsSuccessful);
        Assert.Contains(
            moduleResult.FailingTypeNames ?? [],
            name => name.EndsWith(nameof(CanaryModuleAccessingForeignInternal), StringComparison.Ordinal));

        var canaryApiEndpointTypes = Types.InAssembly(TestAssembly)
            .That()
            .HaveName(nameof(CanaryApiEndpointReferencingGateway));

        var apiResult = LayerDependencyTests.EvaluateApiProcessSeparation(canaryApiEndpointTypes);

        Assert.False(apiResult.IsSuccessful);
        Assert.Contains(
            apiResult.FailingTypeNames ?? [],
            name => name.EndsWith(nameof(CanaryApiEndpointReferencingGateway), StringComparison.Ordinal));
    }

    [Fact]
    public void Regla3FallaCuandoUnTipoFueraDeQueryStoreGatewayUsaSqlClient()
    {
        var canaryRogueSqlTypes = Types.InAssembly(TestAssembly)
            .That()
            .HaveName(nameof(CanaryRogueSqlClientConsumer));

        var sqlClientResult = LayerDependencyTests.EvaluateNoSqlClientDependency(canaryRogueSqlTypes);

        Assert.False(sqlClientResult.IsSuccessful);
        Assert.Contains(
            sqlClientResult.FailingTypeNames ?? [],
            name => name.EndsWith(nameof(CanaryRogueSqlClientConsumer), StringComparison.Ordinal));
    }
}

/// <summary>
/// Canario que rompe a propósito la Regla 1 (Dominio acoplado a EF Core).
/// </summary>
internal sealed class CanaryDomainEntityWithEfCore
{
    public static Type ForbiddenDependency => typeof(DbContext);
}

/// <summary>
/// Canario que rompe a propósito la Regla 1 (Motor determinista acoplado a E/S por red).
/// </summary>
internal sealed class CanaryEngineRuleWithHttpAndFileIo
{
    public static Type ForbiddenDependency => typeof(HttpClient);
}

/// <summary>
/// Canario que rompe a propósito la Regla 2 (Módulo que accede a la implementación interna de otro módulo).
/// </summary>
internal sealed class CanaryModuleAccessingForeignInternal
{
    public static Type ForbiddenInternalType => typeof(SecretProtector);
}

/// <summary>
/// Canario que rompe a propósito la Regla 2 (Endpoint de Api que referencia directamente QueryStoreGateway).
/// </summary>
internal sealed class CanaryApiEndpointReferencingGateway
{
    public static Type ForbiddenGatewayType => typeof(QueryStoreGateway);
}

/// <summary>
/// Canario que rompe a propósito la Regla 3 (Clase ajena a QueryStoreGateway que usa Microsoft.Data.SqlClient).
/// </summary>
internal sealed class CanaryRogueSqlClientConsumer
{
    public static SqlConnection CreateForbiddenConnection() => new();
}
