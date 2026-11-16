using System.Security.Cryptography;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Ai;
using QueryDoctor.Infrastructure.Telemetry;
using QueryDoctor.LabTests;
using Testcontainers.PostgreSql;

namespace QueryDoctor.AcceptanceTests;

/// <summary>
/// La plataforma completa para la suite de aceptación (S3-06): PostgreSQL desechable, la Api real y un Worker armado con
/// <see cref="DependencyInjection.AddWorkerServices"/>, el mismo método que usa <c>QueryDoctor.Worker/Program.cs</c>.
/// La Api encola en Hangfire y el Worker consume de la misma base: es el camino de producción, no una llamada directa.
/// </summary>
/// <remarks>
/// Sin QD_LAB_SQL no arranca nada (las pruebas salen Skipped). La excepción de TLS del laboratorio (invariante 3) se
/// declara solo aquí y solo para el servidor del laboratorio y su alias: el certificado del contenedor es autofirmado.
/// </remarks>
public sealed class AcceptanceFixture : IAsyncLifetime, IDisposable
{
    private readonly string _masterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private PostgreSqlContainer? _postgres;
    private AcceptanceApi? _api;
    private IHost? _worker;

    internal AiServiceDouble Ai { get; } = new();

    internal AcceptanceApi Api => _api ?? throw new InvalidOperationException(LabEnvironment.SkipReason);

    /// <summary>Servidor del laboratorio tal como lo da QD_LAB_SQL.</summary>
    public static string Server => LabEnvironment.RequiredServer;

    /// <summary>
    /// El mismo servidor escrito de otra forma. La unicidad de conexiones es por (servidor, base) en texto, sin DNS (D1
    /// de S1-09): con el alias se registra QD_Lab otra vez con otra cuenta sin chocar con la conexión principal.
    /// </summary>
    public static string ServerAlias => "tcp:" + Server;

    public string PostgresConnectionString => _postgres?.GetConnectionString()
        ?? throw new InvalidOperationException(LabEnvironment.SkipReason);

    /// <summary>Sentencias que el Worker emitió contra SQL Server (RF-TRZ-04, CA-HU15.2).</summary>
    internal InMemoryStatementLogWriter StatementLog =>
        (InMemoryStatementLogWriter)(_worker ?? throw new InvalidOperationException(LabEnvironment.SkipReason))
            .Services.GetRequiredService<IStatementLogWriter>();

    public async Task InitializeAsync()
    {
        if (!LabEnvironment.IsEnabled)
        {
            return;
        }

        _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").WithDatabase("querydoctor_acceptance").Build();
        await _postgres.StartAsync();

        // La Api arranca primero: aplica las migraciones (Database:MigrateOnStartup), como en el compose.
        _api = new AcceptanceApi(PostgresConnectionString, _masterKey);
        _ = _api.Services;

        _worker = BuildWorker();

        // Crea el esquema de Hangfire antes de que el servidor (con PrepareSchemaIfNecessary = false) empiece a sondear.
        _ = _worker.Services.GetRequiredService<JobStorage>();
        await _worker.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_worker is not null)
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _worker.StopAsync(stop.Token);
            _worker.Dispose();
            _worker = null;
        }

        if (_api is not null)
        {
            await _api.DisposeAsync();
            _api = null;
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
            _postgres = null;
        }

        Ai.Dispose();
    }

    /// <summary>xUnit llama primero a <see cref="DisposeAsync"/>; esto solo libera lo que hubiera quedado.</summary>
    public void Dispose()
    {
        _worker?.Dispose();
        _api?.Dispose();
        Ai.Dispose();
    }

    private IHost BuildWorker()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Development",
            DisableDefaults = true,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:QueryDoctor"] = PostgresConnectionString,
            ["Security:SecretProtector:MasterKey"] = _masterKey,
            ["Probe:LabTrustedCertificateServers:0"] = Server,
            ["Probe:LabTrustedCertificateServers:1"] = ServerAlias,
            ["AiService:BaseUrl"] = AiServiceDouble.BaseAddress.ToString(),
        });

        builder.Services.AddWorkerServices(builder.Configuration);

        // Única sustitución: el transporte hacia la IA. El cliente real, el enmascarado y el validador quedan.
        builder.Services.RemoveAll<IAiExplanationClient>();
        builder.Services.AddSingleton<IAiExplanationClient>(sp => new HttpAiExplanationClient(
            new HttpClient(Ai, disposeHandler: false) { BaseAddress = AiServiceDouble.BaseAddress },
            sp.GetRequiredService<AiServiceOptions>()));

        // Un solo servidor para todas las colas, con el sondeo de 1 s de probe/explain: la suite no espera 15 s por cola.
        builder.Services.AddHangfireServer(
            (_, options) =>
            {
                options.ServerName = "QueryDoctor.AcceptanceWorker";
                options.Queues = JobQueues.AllInPriorityOrder;
                options.WorkerCount = 4;
            },
            DependencyInjection.CreateProbeQueueStorage(PostgresConnectionString));

        return builder.Build();
    }
}

[CollectionDefinition(Name)]
public sealed class AcceptanceGroup : ICollectionFixture<AcceptanceFixture>
{
    public const string Name = "Aceptación";
}
