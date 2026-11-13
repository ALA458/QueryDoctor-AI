using Hangfire;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Connections;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQueryDoctorHangfire(builder.Configuration);

// Solo el Worker llega a bases objetivo (invariante 2): Gateway, CapabilityProbe y probe_connection (S1-09).
builder.Services.AddTargetTelemetry(builder.Configuration);

// Invariante 3: la excepción de TLS del laboratorio (docker-compose.lab.yml) no arranca fuera de Development.
LabCertificateGuard.EnsureAllowed(builder.Configuration, builder.Environment.EnvironmentName);

var postgresConn = DependencyInjection.GetPostgresConnectionString(builder.Configuration);
if (!string.IsNullOrWhiteSpace(postgresConn))
{
    // Servidor exclusivo de procesamiento en segundo plano (S1-10, ADR-04, §5.3)
    builder.Services.AddHangfireServer(options =>
    {
        options.ServerName = "QueryDoctor.Worker";
        options.Queues = JobQueues.AllInPriorityOrder.Where(q => q != JobQueues.Probe).ToArray();
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
}

var host = builder.Build();
host.Run();
