using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QueryDoctor.Infrastructure.Persistence;

/// <summary>
/// Solo para las herramientas de EF (dotnet ef): crea el contexto sin arrancar la aplicación.
/// Generar migraciones no se conecta a la base. Para aplicarlas contra una base real, define la
/// variable de entorno QD_DB_CONNECTION en tu terminal; nunca pongas contraseñas en el repo (RNF-SEG-04).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DefaultConnection = "Host=localhost;Database=querydoctor_design_time";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QD_DB_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DefaultConnection;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext((DbContextOptions<AppDbContext>)options);
    }
}
