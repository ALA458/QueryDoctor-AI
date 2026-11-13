using System.Reflection;
using NetArchTest.Rules;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure.Telemetry;

namespace QueryDoctor.ArchitectureTests;

/// <summary>
/// Pruebas de cumplimiento arquitectónico con NetArchTest (S1-03, ADR-01, RNF-MAN-01, RNF-SEG-01, Arq. §5 y §8 F1).
/// Vigila las tres invariantes estructurales de la solución:
/// 1. Dirección de capas y pureza de Domain y del motor determinista (sin EF Core, SqlClient, ASP.NET ni E/S).
/// 2. Aislamiento entre módulos verticales (sin acceso a internos ajenos) y separación de procesos en Api.
/// 3. Exclusividad de Microsoft.Data.SqlClient únicamente en QueryStoreGateway.
/// </summary>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = Assembly.Load("QueryDoctor.Domain");
    private static readonly Assembly Application = Assembly.Load("QueryDoctor.Application");
    private static readonly Assembly Infrastructure = Assembly.Load("QueryDoctor.Infrastructure");
    private static readonly Assembly Api = Assembly.Load("QueryDoctor.Api");
    private static readonly Assembly Worker = Assembly.Load("QueryDoctor.Worker");

    // =========================================================================
    // REGLA 1: Dominio sin EF Core / frameworks, dirección de capas y motor sin E/S
    // =========================================================================

    [Fact]
    public void DomainNoDependeDeOtrasCapasNiDeFrameworks()
    {
        var result = EvaluateDomainPurity(Types.InAssembly(Domain));

        Assert.True(result.IsSuccessful, Describe(result));
    }

    // S1-09: los casos de uso persisten y encolan a través de interfaces propias
    // (IConnectionRepository, IProbeJobScheduler); EF Core, Npgsql y Hangfire viven en Infrastructure.
    [Fact]
    public void ApplicationNoDependeDeInfrastructureNiDePersistenciaNiDeHangfire()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Infrastructure",
                "QueryDoctor.Api",
                "QueryDoctor.Worker",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Hangfire")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void ApplicationNoDependeDeInfraestructuraNiDeHostsNiDeFrameworksDePersistencia()
    {
        var result = EvaluateApplicationLayerIsolation(Types.InAssembly(Application));

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void InfrastructureNoDependeDeLosHostsApiNiWorker()
    {
        var result = EvaluateInfrastructureHostIsolation(Types.InAssembly(Infrastructure));

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void MotorDeterministicoEsPuroYNoDependeDeFrameworksDeEntradaSalida()
    {
        var engineTypes = Types.InAssembly(Application)
            .That()
            .ResideInNamespace("QueryDoctor.Application.Engine")
            .Or()
            .ResideInNamespace("QueryDoctor.Application.Scoring")
            .Or()
            .ResideInNamespace("QueryDoctor.Application.Validation");

        var result = EvaluateDeterministicEnginePurity(engineTypes);

        Assert.True(result.IsSuccessful, Describe(result));
    }

    // =========================================================================
    // REGLA 2: Módulos sin acceso a internos ajenos y separación de procesos
    // =========================================================================

    [Fact]
    public void ModulosVerticalesNoAccedenAInternosAjenos()
    {
        // 1.En Application: Engine, Telemetry y Jobs no se acoplan entre sí
        var engineIsolation = EvaluateModuleIsolation(
            Types.InAssembly(Application).That().ResideInNamespace("QueryDoctor.Application.Engine"),
            "QueryDoctor.Application.Telemetry",
            "QueryDoctor.Application.Jobs",
            "QueryDoctor.Application.Ingestion",
            "QueryDoctor.Application.Connections");
        Assert.True(engineIsolation.IsSuccessful, $"Application.Engine: {Describe(engineIsolation)}");

        var telemetryAppIsolation = EvaluateModuleIsolation(
            Types.InAssembly(Application).That().ResideInNamespace("QueryDoctor.Application.Telemetry"),
            "QueryDoctor.Application.Engine",
            "QueryDoctor.Application.Jobs",
            "QueryDoctor.Application.Scoring",
            "QueryDoctor.Application.Validation");
        Assert.True(telemetryAppIsolation.IsSuccessful, $"Application.Telemetry: {Describe(telemetryAppIsolation)}");

        var jobsAppIsolation = EvaluateModuleIsolation(
            Types.InAssembly(Application).That().ResideInNamespace("QueryDoctor.Application.Jobs"),
            "QueryDoctor.Application.Engine",
            "QueryDoctor.Application.Telemetry",
            "QueryDoctor.Application.Scoring",
            "QueryDoctor.Application.Validation");
        Assert.True(jobsAppIsolation.IsSuccessful, $"Application.Jobs: {Describe(jobsAppIsolation)}");

        // 2.En Infrastructure: Telemetry, Security y Persistence no acceden a internos de otros submódulos
        var telemetryInfraIsolation = EvaluateModuleIsolation(
            Types.InAssembly(Infrastructure).That().ResideInNamespace("QueryDoctor.Infrastructure.Telemetry"),
            "QueryDoctor.Infrastructure.Security",
            "QueryDoctor.Infrastructure.Persistence",
            "QueryDoctor.Infrastructure.Ai");
        Assert.True(telemetryInfraIsolation.IsSuccessful, $"Infrastructure.Telemetry: {Describe(telemetryInfraIsolation)}");

        var securityInfraIsolation = EvaluateModuleIsolation(
            Types.InAssembly(Infrastructure).That().ResideInNamespace("QueryDoctor.Infrastructure.Security"),
            "QueryDoctor.Infrastructure.Telemetry",
            "QueryDoctor.Infrastructure.Persistence",
            "QueryDoctor.Infrastructure.Ai");
        Assert.True(securityInfraIsolation.IsSuccessful, $"Infrastructure.Security: {Describe(securityInfraIsolation)}");

        var persistenceInfraIsolation = EvaluateModuleIsolation(
            Types.InAssembly(Infrastructure).That().ResideInNamespace("QueryDoctor.Infrastructure.Persistence"),
            "QueryDoctor.Infrastructure.Telemetry",
            "QueryDoctor.Infrastructure.Security",
            "QueryDoctor.Infrastructure.Ai");
        Assert.True(persistenceInfraIsolation.IsSuccessful, $"Infrastructure.Persistence: {Describe(persistenceInfraIsolation)}");
    }

    [Fact]
    public void ApiNoReferenciaGatewayDeBasesObjetivoNiClienteDeIa()
    {
        var result = EvaluateApiProcessSeparation(Types.InAssembly(Api));

        Assert.True(result.IsSuccessful, Describe(result));
    }

    // =========================================================================
    // REGLA 3: Solo QueryStoreGateway usa Microsoft.Data.SqlClient (RNF-SEG-01)
    // =========================================================================

    [Fact]
    public void SoloQueryStoreGatewayUsaSqlClientEnTodaLaSolucion()
    {
        // 1. Ningún ensamblado fuera de Infrastructure puede referenciar Microsoft.Data.SqlClient
        foreach (var assembly in new[] { Domain, Application, Api, Worker })
        {
            var assemblyResult = EvaluateNoSqlClientDependency(Types.InAssembly(assembly));
            Assert.True(assemblyResult.IsSuccessful, $"{assembly.GetName().Name}: {Describe(assemblyResult)}");
        }

        // 2. Dentro de Infrastructure, ningún tipo fuera de QueryStoreGateway puede referenciar Microsoft.Data.SqlClient
        var nonGatewayInfraTypes = Types.InAssembly(Infrastructure)
            .That()
            .DoNotHaveName("QueryStoreGateway")
            .And()
            .DoNotHaveNameStartingWith("<"); // Excluye máquinas de estado async anidadas en QueryStoreGateway

        var infraResult = EvaluateNoSqlClientDependency(nonGatewayInfraTypes);
        Assert.True(infraResult.IsSuccessful, $"QueryDoctor.Infrastructure (fuera de QueryStoreGateway): {Describe(infraResult)}");

        // 3. Además, en otros namespaces de Infrastructure (Persistence, Security, etc.) ni siquiera máquinas async pueden usar SqlClient
        var nonTelemetryInfraTypes = Types.InAssembly(Infrastructure)
            .That()
            .DoNotResideInNamespace("QueryDoctor.Infrastructure.Telemetry");

        var nonTelemetryResult = EvaluateNoSqlClientDependency(nonTelemetryInfraTypes);
        Assert.True(nonTelemetryResult.IsSuccessful, $"QueryDoctor.Infrastructure (fuera de Telemetry): {Describe(nonTelemetryResult)}");

        // 4. Verificación positiva: QueryStoreGateway existe, es sellada e implementa ITelemetrySource
        Assert.True(typeof(QueryStoreGateway).IsSealed);
        Assert.True(typeof(ITelemetrySource).IsAssignableFrom(typeof(QueryStoreGateway)));
    }

    // =========================================================================
    // Evaluadores reutilizables (usados también por las pruebas canario negativas)
    // =========================================================================

    internal static TestResult EvaluateDomainPurity(Types types) =>
        types
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Application",
                "QueryDoctor.Infrastructure",
                "QueryDoctor.Api",
                "QueryDoctor.Worker",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.Data.SqlClient",
                "Microsoft.AspNetCore",
                "Hangfire")
            .GetResult();

    internal static TestResult EvaluateDomainPurity(PredicateList types) =>
        types
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Application",
                "QueryDoctor.Infrastructure",
                "QueryDoctor.Api",
                "QueryDoctor.Worker",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.Data.SqlClient",
                "Microsoft.AspNetCore",
                "Hangfire")
            .GetResult();

    internal static TestResult EvaluateApplicationLayerIsolation(Types types) =>
        types
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Infrastructure",
                "QueryDoctor.Api",
                "QueryDoctor.Worker",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.Data.SqlClient",
                "Microsoft.AspNetCore")
            .GetResult();

    internal static TestResult EvaluateInfrastructureHostIsolation(Types types) =>
        types
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Api",
                "QueryDoctor.Worker")
            .GetResult();

    internal static TestResult EvaluateDeterministicEnginePurity(PredicateList engineTypes) =>
        engineTypes
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Infrastructure",
                "QueryDoctor.Api",
                "QueryDoctor.Worker",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.Data.SqlClient",
                "System.Net.Http",
                "System.IO.File",
                "System.IO.Directory",
                "Hangfire")
            .GetResult();

    internal static TestResult EvaluateModuleIsolation(PredicateList moduleTypes, params string[] forbiddenNamespaces) =>
        moduleTypes
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

    internal static TestResult EvaluateApiProcessSeparation(Types apiTypes) =>
        apiTypes
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Infrastructure.Telemetry",
                "QueryDoctor.Application.Telemetry",
                "QueryDoctor.Infrastructure.Ai",
                "Microsoft.Data.SqlClient")
            .GetResult();

    internal static TestResult EvaluateApiProcessSeparation(PredicateList apiTypes) =>
        apiTypes
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Infrastructure.Telemetry",
                "QueryDoctor.Application.Telemetry",
                "QueryDoctor.Infrastructure.Ai",
                "Microsoft.Data.SqlClient")
            .GetResult();

    internal static TestResult EvaluateNoSqlClientDependency(Types types) =>
        types
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Data.SqlClient")
            .GetResult();

    internal static TestResult EvaluateNoSqlClientDependency(PredicateList types) =>
        types
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Data.SqlClient")
            .GetResult();

    internal static string Describe(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Tipos que violan la regla: " + string.Join(", ", result.FailingTypeNames ?? []);
}
