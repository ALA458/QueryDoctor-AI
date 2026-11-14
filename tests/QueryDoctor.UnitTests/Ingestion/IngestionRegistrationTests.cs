using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Ingestion;

namespace QueryDoctor.UnitTests.Ingestion;

// El Worker arma el trabajo ingest_connection con su DI real; la Api no lo puede armar (invariante 2).
public class IngestionRegistrationTests
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

    [Fact]
    public void ElWorkerResuelveElTrabajoConLasOpcionesConfiguradas()
    {
        var configuration = Configuration(new() { ["Ingestion:TopN"] = "25" });
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .AddTargetTelemetry(configuration)
            .AddIngestion(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IngestConnectionJob>());
        Assert.IsType<PostgresIngestionLock>(scope.ServiceProvider.GetRequiredService<IIngestionLock>());
        Assert.Equal(25, scope.ServiceProvider.GetRequiredService<IngestionOptions>().TopN);
    }

    [Fact]
    public void UnTopNInvalidoImpideArmarLaIngesta()
    {
        var configuration = Configuration(new() { ["Ingestion:TopN"] = "0" });
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .AddTargetTelemetry(configuration)
            .AddIngestion(configuration)
            .BuildServiceProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => provider.GetRequiredService<IngestionOptions>());
    }

    [Fact]
    public void LaApiSoloPuedeEncolar()
    {
        var configuration = Configuration();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .AddIngestionRequests()
            .BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IngestConnection>());
        Assert.Null(scope.ServiceProvider.GetService<IQueryStoreReader>());
        Assert.NotNull(scope.ServiceProvider.GetService<IIngestionQueries>());
    }
}
