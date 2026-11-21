using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Connections;
using QueryDoctor.Infrastructure.Explanations;
using QueryDoctor.Infrastructure.Ingestion;
using QueryDoctor.Infrastructure.Retention;
using QueryDoctor.Infrastructure.Scoring;

namespace QueryDoctor.UnitTests.Jobs;

// S3-06: AddWorkerServices es la composición única del Worker (Program.cs y la suite de aceptación la comparten).
public sealed class WorkerServicesRegistrationTests
{
    private static ServiceProvider Worker() => new ServiceCollection()
        .AddLogging()
        .AddWorkerServices(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:QueryDoctor"] = "Host=localhost;Database=qd;Username=qd;Password=no-se-conecta",
            ["Security:SecretProtector:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["AiService:BaseUrl"] = "http://ai:8000/",
        }).Build())
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Fact]
    public void ArmaLosCincoTrabajosDelWorkerConAccesoSistema()
    {
        using var provider = Worker();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<ProbeConnectionJob>());
        Assert.NotNull(services.GetRequiredService<IngestConnectionJob>());
        Assert.NotNull(services.GetRequiredService<RecalculateScoresJob>());
        Assert.NotNull(services.GetRequiredService<ExplainQueryJob>());
        Assert.NotNull(services.GetRequiredService<RetentionJob>());

        // S2-12: el Worker ve todas las conexiones; nunca se deduce de "no hay usuario".
        Assert.Same(SystemAccessScope.Instance, services.GetRequiredService<IAccessScope>());
    }

    [Fact]
    public void IncluyeGatewayEnmascaradorYClienteDeIa()
    {
        // Invariante 2: lo que la Api no puede tener, el Worker sí lo tiene.
        using var provider = Worker();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITelemetrySource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMasker>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IAiExplanationClient>());
    }
}
