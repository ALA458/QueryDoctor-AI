using System.Reflection;
using NetArchTest.Rules;

namespace QueryDoctor.ArchitectureTests;

// Reglas de dependencia del documento de arquitectura (§5) y del invariante de
// solo lectura (RNF-SEG-01). El compilador no detecta referencias a paquetes
// prohibidos; estas pruebas sí.
public class LayerDependencyTests
{
    private static readonly Assembly Domain = Assembly.Load("QueryDoctor.Domain");
    private static readonly Assembly Application = Assembly.Load("QueryDoctor.Application");
    private static readonly Assembly Api = Assembly.Load("QueryDoctor.Api");
    private static readonly Assembly Worker = Assembly.Load("QueryDoctor.Worker");

    [Fact]
    public void DomainNoDependeDeOtrasCapasNiDeFrameworks()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Application",
                "QueryDoctor.Infrastructure",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Data.SqlClient",
                "Microsoft.AspNetCore")
            .GetResult();

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
    public void SoloInfrastructureUsaSqlClient()
    {
        foreach (var assembly in new[] { Domain, Application, Api, Worker })
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("Microsoft.Data.SqlClient")
                .GetResult();

            Assert.True(result.IsSuccessful, $"{assembly.GetName().Name}: {Describe(result)}");
        }
    }

    private static string Describe(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : "Tipos que violan la regla: " + string.Join(", ", result.FailingTypeNames ?? []);
}
