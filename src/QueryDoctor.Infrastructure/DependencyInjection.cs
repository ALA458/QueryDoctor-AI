using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Engine.Parsing;
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

        // Motor determinístico: parser de Showplan XML en memoria sin E/S (S1-12, RF-MOT-01, RF-MOT-16)
        services.TryAddSingleton<PlanParserOptions>();
        services.TryAddSingleton<IPlanParser, ShowplanXmlParser>();

        // Telemetría e ingesta de SQL Server (S1-08, RNF-SEG-01, RF-TRZ-04)
        services.TryAddSingleton<IStatementLogWriter, InMemoryStatementLogWriter>();
        services.AddScoped<ITelemetrySource, QueryStoreGateway>();

        // Persistencia en PostgreSQL (S1-04). Solo se registra si hay cadena de conexión,
        // así los hosts que aún no la necesitan siguen arrancando igual.
        // En Docker la contraseña llega como secreto (archivo), nunca dentro de una cadena versionada (RNF-SEG-04).
        var connectionString = configuration.GetConnectionString("QueryDoctor")
            ?? BuildConnectionStringFromSecret(configuration);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
        }

        return services;
    }

    private static string? BuildConnectionStringFromSecret(IConfiguration configuration)
    {
        var section = configuration.GetSection("Postgres");
        var passwordFile = section["PasswordFile"];
        if (string.IsNullOrWhiteSpace(passwordFile) || !File.Exists(passwordFile))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = section["Host"] ?? "db",
            Port = int.TryParse(section["Port"], CultureInfo.InvariantCulture, out var port) ? port : 5432,
            Database = section["Database"] ?? "querydoctor",
            Username = section["Username"] ?? "querydoctor",
            Password = File.ReadAllText(passwordFile).Trim(),
        };

        return builder.ConnectionString;
    }
}
