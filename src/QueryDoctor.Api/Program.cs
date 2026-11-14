using System.Text.Json.Serialization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using QueryDoctor.Api.Auth;
using QueryDoctor.Api.Connections;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQueryDoctorHangfire(builder.Configuration);
builder.Services.AddQueryDoctorAuth(builder.Configuration);

// S1-09: alta de conexiones. La Api guarda y encola; NO registra el Gateway (invariante 2).
builder.Services.AddConnectionRegistration();

// S1-11: ingesta manual. La Api valida y encola ingest_connection; la ingesta corre en el Worker (invariante 2).
builder.Services.AddIngestionRequests();

// Enumerados como texto en las respuestas (contrato de S1-15), nunca como número.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var migrationScope = app.Services.CreateScope();
    var db = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

using (var seedScope = app.Services.CreateScope())
{
    await InitialDbaSeeder.SeedAsync(seedScope.ServiceProvider, app.Configuration);
}

app.UseSameOriginForms();
app.UseAuthentication();
app.UseAuthorization();

// Panel Hangfire (S1-10, ADR-04, RNF-SEG-12): solo DBA con contraseña activa
var postgresConn = DependencyInjection.GetPostgresConnectionString(app.Configuration);
if (!string.IsNullOrWhiteSpace(postgresConn))
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new HangfireDashboardAuthorizationFilter()],
        DashboardTitle = "Query Doctor AI - Panel Hangfire"
    });
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Healthcheck del contenedor: debe ser anónimo. Todo lo demás exige sesión (FallbackPolicy).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapConnectionEndpoints();

app.Run();

/// <summary>Visible para WebApplicationFactory en las pruebas de integración (S1-09).</summary>
public partial class Program;
