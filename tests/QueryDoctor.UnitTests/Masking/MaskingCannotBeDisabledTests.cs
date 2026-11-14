using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Masking;
using QueryDoctor.Infrastructure;
using static QueryDoctor.UnitTests.Masking.MaskingSentinels;

namespace QueryDoctor.UnitTests.Masking;

/// <summary>
/// CA-HU21.4 y RNF-SEG-05 ("no existirá configuración que desactive esta sustitución"): ni opciones, ni flag,
/// ni variable de entorno, ni clave de configuración apagan el enmascarado.
/// </summary>
public sealed class MaskingCannotBeDisabledTests
{
    private static readonly string[] ForbiddenConfigurationTerms = ["mask", "enmascar", "literal"];

    [Fact]
    public void ElEnmascaradorNoTieneOpcionesParametrosNiPropiedadesConfigurables()
    {
        var constructors = typeof(LiteralMasker).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        var constructor = Assert.Single(constructors);
        Assert.Empty(constructor.GetParameters());

        Assert.Empty(typeof(LiteralMasker).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));

        var mask = Assert.Single(typeof(IMasker).GetMethods());
        var parameter = Assert.Single(mask.GetParameters());
        Assert.Equal(typeof(MaskingInput), parameter.ParameterType);

        // La entrada solo trae contenido: ningún bool, enumeración ni objeto de opciones.
        Assert.All(
            typeof(MaskingInput).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract"),
            property => Assert.True(
                property.PropertyType == typeof(string) ||
                property.PropertyType == typeof(IReadOnlyList<Application.Engine.Rules.FindingContract>),
                $"MaskingInput.{property.Name} no es contenido: {property.PropertyType}"));

        var typesInMasking = typeof(LiteralMasker).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(LiteralMasker).Namespace)
            .Select(t => t.Name);
        Assert.DoesNotContain(typesInMasking, name =>
            name.Contains("Option", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Setting", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Config", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NingunArchivoDeConfiguracionNiVariableDeEntornoDeclaraUnaClaveDeEnmascarado()
    {
        var root = RepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(root, "docker-compose*.yml"))
            .Concat(Directory.EnumerateFiles(root, ".env.example"))
            .ToArray();

        Assert.NotEmpty(files);
        Assert.Contains(files, path => Path.GetFileName(path) == "docker-compose.yml");
        Assert.Contains(files, path => Path.GetFileName(path) == "appsettings.json");

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            foreach (var term in ForbiddenConfigurationTerms)
            {
                Assert.False(
                    content.Contains(term, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetRelativePath(root, file)} contiene '{term}': no puede existir configuración del enmascarado.");
            }
        }
    }

    [Fact]
    public void ConClavesQueIntentanApagarloElEnmascaradorRegistradoSigueEnmascarando()
    {
        var toggles = new Dictionary<string, string?>
        {
            ["Masking:Enabled"] = "false",
            ["Masking:Disabled"] = "true",
            ["LiteralMasker:Enabled"] = "false",
            ["LiteralMasker__Disabled"] = "true",
            ["RNF_SEG_05_OFF"] = "1",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(toggles).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLiteralMasking();

        using var provider = services.BuildServiceProvider();
        var masker = provider.GetRequiredService<IMasker>();

        Assert.IsType<LiteralMasker>(masker);
        var result = masker.Mask(new MaskingInput($"SELECT '{Secret}' /* {CommentSecret} */, {BigNumber}"));
        Assert.Equal("SELECT @qd_str_1  , @qd_num_2", result.Sql.Text);
        AssertNoneIn(result.Sql.Text);
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "QueryDoctor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }
}
