using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Ai;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Ai;
using QueryDoctor.Infrastructure.Explanations;

namespace QueryDoctor.UnitTests.Ai;

/// <summary>El Worker arma el trabajo explain con su DI real (S2-05); sin dirección del servicio de IA arranca igual.</summary>
public sealed class ExplanationRegistrationTests
{
    private static IConfiguration Configuration(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:QueryDoctor"] = "Host=localhost;Database=qd;Username=qd;Password=no-se-conecta",
            ["Security:SecretProtector:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };
        foreach (var (key, value) in extra ?? [])
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider Worker(IConfiguration configuration) => new ServiceCollection()
        .AddLogging()
        .AddInfrastructure(configuration)
        .AddLiteralMasking()
        .AddExplanations(configuration)
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Fact]
    public void ElWorkerResuelveElTrabajoExplainConLasOpcionesConfiguradas()
    {
        using var provider = Worker(Configuration(new()
        {
            ["AiService:BaseUrl"] = "http://ai:8000/",
            ["AiService:TimeoutSeconds"] = "45",
        }));

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ExplainQueryJob>());
        Assert.IsType<HttpAiExplanationClient>(scope.ServiceProvider.GetRequiredService<IAiExplanationClient>());
        Assert.Equal(45, scope.ServiceProvider.GetRequiredService<AiServiceOptions>().TimeoutSeconds);
    }

    [Fact]
    public void SinDireccionDelServicioDeIaElWorkerArrancaIgual()
    {
        // RF-IA-06: sin IA configurada, las explicaciones quedan "no disponible", pero nada más se rompe (CA-HU12.2).
        using var provider = Worker(Configuration());

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ExplainQueryJob>());
    }

    [Fact]
    public void UnaDireccionInvalidaImpideArmarElCliente()
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(Configuration(new() { ["AiService:BaseUrl"] = "ftp://ai/" }))
            .AddLiteralMasking()
            .AddExplanations(Configuration(new() { ["AiService:BaseUrl"] = "ftp://ai/" }))
            .BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IAiExplanationClient>());
    }

    [Fact]
    public void LaApiNoPuedeArmarElTrabajoNiElCliente()
    {
        var configuration = Configuration();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<ExplainQuery>());
        Assert.Null(scope.ServiceProvider.GetService<IAiExplanationClient>());
    }
}
