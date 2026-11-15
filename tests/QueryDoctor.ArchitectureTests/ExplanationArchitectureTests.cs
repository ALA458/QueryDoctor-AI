using System.Reflection;
using NetArchTest.Rules;

namespace QueryDoctor.ArchitectureTests;

/// <summary>
/// Reglas de arquitectura de las explicaciones (S2-05, invariante 2; deuda H4 de S2-04):
/// 1. La Api no usa el enmascarador ni el puerto hacia la IA: solo <c>Application.Explanations</c>.
/// 2. <c>Application.Explanations</c> no depende de <c>Application.Ai</c> ni de <c>Masking</c>, así la Api no los
///    alcanza de forma indirecta.
/// 3. <c>Application.Ai</c> no hace E/S: la red y la base quedan en Infrastructure.
/// </summary>
public sealed class ExplanationArchitectureTests
{
    private static readonly Assembly Api = Assembly.Load("QueryDoctor.Api");
    private static readonly Assembly Application = Assembly.Load("QueryDoctor.Application");

    [Fact]
    public void LaApiNoUsaElEnmascaradorNiElPuertoHaciaLaIa()
    {
        var result = Types.InAssembly(Api)
            .ShouldNot()
            .HaveDependencyOnAny(
                "QueryDoctor.Application.Ai",
                "QueryDoctor.Application.Masking",
                "QueryDoctor.Infrastructure.Ai",
                "QueryDoctor.Infrastructure.Explanations")
            .GetResult();

        Assert.True(result.IsSuccessful, LayerDependencyTests.Describe(result));
    }

    [Fact]
    public void LoQueUsaLaApiNoAlcanzaLaIaNiElEnmascarador()
    {
        var result = Types.InAssembly(Application)
            .That()
            .ResideInNamespace("QueryDoctor.Application.Explanations")
            .ShouldNot()
            .HaveDependencyOnAny("QueryDoctor.Application.Ai", "QueryDoctor.Application.Masking")
            .GetResult();

        Assert.True(result.IsSuccessful, LayerDependencyTests.Describe(result));
    }

    [Fact]
    public void ElModuloDeIaDeApplicationNoHaceEntradaSalida()
    {
        var result = Types.InAssembly(Application)
            .That()
            .ResideInNamespace("QueryDoctor.Application.Ai")
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

    [Fact]
    public void LaReglaDeLaApiDetectaUnUsoDelEnmascarador()
    {
        // Canario: este tipo depende de Masking, así que la misma regla debe fallar sobre él.
        var result = Types.InAssembly(typeof(MaskingCanaryForApiRule).Assembly)
            .That()
            .HaveNameStartingWith(nameof(MaskingCanaryForApiRule))
            .ShouldNot()
            .HaveDependencyOnAny("QueryDoctor.Application.Masking")
            .GetResult();

        Assert.False(result.IsSuccessful);
    }
}

internal static class MaskingCanaryForApiRule
{
    public static Type Masker => typeof(QueryDoctor.Application.Masking.LiteralMasker);
}
