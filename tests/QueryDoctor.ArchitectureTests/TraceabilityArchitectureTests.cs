using System.Reflection;
using NetArchTest.Rules;

namespace QueryDoctor.ArchitectureTests;

/// <summary>
/// S2-06: la cadena de hash y su verificador viven en Application sin E/S, para que sean deterministas y probables
/// sin base; la base y el bloqueo consultivo quedan en Infrastructure.
/// </summary>
public sealed class TraceabilityArchitectureTests
{
    [Fact]
    public void LaCadenaDeTrazabilidadDeApplicationNoHaceEntradaSalida()
    {
        var result = Types.InAssembly(Assembly.Load("QueryDoctor.Application"))
            .That()
            .ResideInNamespace("QueryDoctor.Application.Traceability")
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
                "Hangfire")
            .GetResult();

        Assert.True(result.IsSuccessful, LayerDependencyTests.Describe(result));
    }
}
