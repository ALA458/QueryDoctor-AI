using System.Reflection;
using NetArchTest.Rules;

namespace QueryDoctor.ArchitectureTests;

/// <summary>
/// Reglas de arquitectura del enmascarador de literales (S2-04, RNF-SEG-05, RNF-SEG-09, D9):
/// 1. <c>QueryDoctor.Application.Masking</c> es puro: sin E/S, sin logging y sin configuración.
/// 2. ScriptDom solo se usa dentro de <c>QueryDoctor.Application.Masking</c>.
/// </summary>
public sealed class MaskingArchitectureTests
{
    private const string MaskingNamespace = "QueryDoctor.Application.Masking";
    private const string ScriptDom = "Microsoft.SqlServer.TransactSql.ScriptDom";

    private static readonly Assembly[] Solution =
    [
        Assembly.Load("QueryDoctor.Domain"),
        Assembly.Load("QueryDoctor.Application"),
        Assembly.Load("QueryDoctor.Infrastructure"),
        Assembly.Load("QueryDoctor.Api"),
        Assembly.Load("QueryDoctor.Worker"),
    ];

    [Fact]
    public void ElEnmascaradorEsPuroYNoRegistraNiLeeConfiguracion()
    {
        var result = Types.InAssembly(Assembly.Load("QueryDoctor.Application"))
            .That()
            .ResideInNamespace(MaskingNamespace)
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
                "System.Console",
                "System.Diagnostics.Debug",
                "System.Diagnostics.Trace",
                "System.Environment",
                "Hangfire",
                "Microsoft.Extensions.Logging",
                "Serilog",
                "Microsoft.Extensions.Configuration",
                "Microsoft.Extensions.Options")
            .GetResult();

        Assert.True(result.IsSuccessful, LayerDependencyTests.Describe(result));
    }

    [Fact]
    public void ScriptDomSoloSeUsaDentroDelEnmascarador()
    {
        var result = Types.InAssemblies(Solution)
            .That()
            .DoNotResideInNamespace(MaskingNamespace)
            .ShouldNot()
            .HaveDependencyOn(ScriptDom)
            .GetResult();

        Assert.True(result.IsSuccessful, LayerDependencyTests.Describe(result));
    }

    [Fact]
    public void LaReglaDeExclusividadDeScriptDomDetectaUnUsoFueraDelEnmascarador()
    {
        // Canario: este mismo tipo usa ScriptDom fuera de Masking, así que la regla debe fallar sobre él.
        var result = Types.InAssembly(typeof(ScriptDomCanary).Assembly)
            .That()
            .HaveNameStartingWith(nameof(ScriptDomCanary))
            .ShouldNot()
            .HaveDependencyOn(ScriptDom)
            .GetResult();

        Assert.False(result.IsSuccessful);
    }
}

internal static class ScriptDomCanary
{
    public static int CountTokens(string text) =>
        new Microsoft.SqlServer.TransactSql.ScriptDom.TSql170Parser(true).GetTokenStream(new StringReader(text), out _).Count;
}
