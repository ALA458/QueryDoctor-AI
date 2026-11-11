using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Security;
using QueryDoctor.Infrastructure.Telemetry;

namespace QueryDoctor.Infrastructure;

/// <summary>
/// Métodos de extensión para registrar servicios de la capa de infraestructura.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Agrega los servicios de infraestructura al contenedor de dependencias.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SecretProtectorOptions>(
            configuration.GetSection(SecretProtectorOptions.SectionName));

        services.AddSingleton<IFieldEncryptor, SecretProtector>();

        // Telemetría e ingesta de SQL Server (S1-08, RNF-SEG-01, RF-TRZ-04)
        services.TryAddSingleton<IStatementLogWriter, InMemoryStatementLogWriter>();
        services.AddScoped<ITelemetrySource, QueryStoreGateway>();

        // Persistencia en PostgreSQL (S1-04). Solo se registra si hay cadena de conexión,
        // así los hosts que aún no la necesitan siguen arrancando igual.
        var connectionString = configuration.GetConnectionString("QueryDoctor");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        }

        return services;
    }
}
