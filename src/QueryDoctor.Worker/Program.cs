using Hangfire;
using QueryDoctor.Application.Jobs;
using QueryDoctor.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQueryDoctorHangfire(builder.Configuration);

var postgresConn = DependencyInjection.GetPostgresConnectionString(builder.Configuration);
if (!string.IsNullOrWhiteSpace(postgresConn))
{
    // Servidor exclusivo de procesamiento en segundo plano (S1-10, ADR-04, §5.3)
    builder.Services.AddHangfireServer(options =>
    {
        options.ServerName = "QueryDoctor.Worker";
        options.Queues = JobQueues.AllInPriorityOrder;
        options.WorkerCount = Math.Max(2, Environment.ProcessorCount * 2);
    });
}

var host = builder.Build();
host.Run();
