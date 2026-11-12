using Microsoft.EntityFrameworkCore;
using QueryDoctor.Api.Auth;
using QueryDoctor.Infrastructure;
using QueryDoctor.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddQueryDoctorAuth(builder.Configuration);

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

// Healthcheck del contenedor: debe ser anónimo. Todo lo demás exige sesión (FallbackPolicy).
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();

app.Run();
