using Hangfire;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Application.Retention;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Connections;
using QueryDoctor.Infrastructure.Retention;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQueryDoctorHangfire(builder.Configuration);

// S2-12 (RNF-SEG-12): el Worker es un proceso de la plataforma e ingiere, sondea y explica todas las conexiones.
// Se declara aquí de forma explícita; AddInfrastructure no registra ningún acceso por defecto.
builder.Services.AddSystemAccessScope();

// Solo el Worker llega a bases objetivo (invariante 2): Gateway, CapabilityProbe y probe_connection (S1-09).
builder.Services.AddTargetTelemetry(builder.Configuration);

// S1-11: ingest_connection en la cola ingest (la atiende el servidor principal de abajo).
builder.Services.AddIngestion(builder.Configuration);

// S2-04: el contexto hacia la IA se arma y enmascara en el Worker (invariante 5). Sin configuración: no se apaga.
builder.Services.AddLiteralMasking();

// S2-05: trabajo explain (único que habla con el servicio de IA, invariante 2), con degradación si la IA falla.
builder.Services.AddExplanations(builder.Configuration);

// S2-13: purga automática por retención de texto SQL y planes (RNF-SEG-10).
builder.Services.AddRetention(builder.Configuration);

// Invariante 3: la excepción de TLS del laboratorio (docker-compose.lab.yml) no arranca fuera de Development.
LabCertificateGuard.EnsureAllowed(builder.Configuration, builder.Environment.EnvironmentName);

var postgresConn = DependencyInjection.GetPostgresConnectionString(builder.Configuration);
if (!string.IsNullOrWhiteSpace(postgresConn))
{
    // Servidor exclusivo de procesamiento en segundo plano (S1-10, ADR-04, §5.3)
    builder.Services.AddHangfireServer(options =>
    {
        options.ServerName = "QueryDoctor.Worker";
        options.Queues = JobQueues.AllInPriorityOrder.Where(q => q != JobQueues.Probe && q != JobQueues.Explain).ToArray();
        options.WorkerCount = Math.Max(2, Environment.ProcessorCount * 2);
    });

    // S1-09, RF-CFG-02: la cola probe tiene su propio servidor con sondeo de 1 s, para que el resultado del
    // sondeo llegue dentro del tiempo límite y no espere los 15 s de sondeo de las demás colas.
    builder.Services.AddHangfireServer(
        (_, options) =>
        {
            options.ServerName = "QueryDoctor.Worker.Probe";
            options.Queues = [JobQueues.Probe];
            options.WorkerCount = 2;
        },
        DependencyInjection.CreateProbeQueueStorage(postgresConn));

    // S2-05, E2, CA-HU09.3: la cola explain también sondea cada 1 s, para que una explicación en caché se vea en
    // ≤ 2 s. Dos trabajadores: cada explicación puede esperar al LLM hasta el tiempo límite.
    builder.Services.AddHangfireServer(
        (_, options) =>
        {
            options.ServerName = "QueryDoctor.Worker.Explain";
            options.Queues = [JobQueues.Explain];
            options.WorkerCount = 2;
        },
        DependencyInjection.CreateExplainQueueStorage(postgresConn));
}

var host = builder.Build();

if (!string.IsNullOrWhiteSpace(postgresConn))
{
    using var scope = host.Services.CreateScope();
    var retention = scope.ServiceProvider.GetRequiredService<RetentionOptions>();
    var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurring.AddOrUpdate<RetentionPurgeJob>(
        "retention-purge",
        JobQueues.Retention,
        job => job.RunAsync(CancellationToken.None),
        retention.Cron,
        new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}

host.Run();
