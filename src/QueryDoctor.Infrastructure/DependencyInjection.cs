using System.Globalization;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure.Connections;
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
        services.TryAddSingleton(TimeProvider.System);

        // S1-09: el Gateway hacia las bases objetivo ya NO se registra aquí, porque la Api también llama a
        // AddInfrastructure y nunca debe conectarse a bases objetivo (invariante 2). Ver AddTargetTelemetry.

        // Persistencia en PostgreSQL (S1-04). Solo se registra si hay cadena de conexión,
        // así los hosts que aún no la necesitan siguen arrancando igual.
        // En Docker la contraseña llega como secreto (archivo), nunca dentro de una cadena versionada (RNF-SEG-04).
        var connectionString = GetPostgresConnectionString(configuration);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
            services.AddScoped<IConnectionRepository, EfConnectionRepository>();
            services.AddScoped<IConnectionQueries, EfConnectionQueries>();
        }

        return services;
    }

    /// <summary>
    /// Alta de conexiones para la Api (S1-09): caso de uso y encolado de probe_connection.
    /// No registra el Gateway: la Api solo guarda y encola.
    /// </summary>
    public static IServiceCollection AddConnectionRegistration(this IServiceCollection services)
    {
        services.AddScoped<IProbeJobScheduler, HangfireProbeJobScheduler>();
        services.AddScoped<RegisterConnection>();
        return services;
    }

    /// <summary>
    /// Acceso a bases objetivo: SOLO para el Worker (invariante 2, §10). Gateway, CapabilityProbe y el trabajo
    /// probe_connection (S1-08, S1-09; RNF-SEG-01, RF-TRZ-04).
    /// </summary>
    public static IServiceCollection AddTargetTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IStatementLogWriter, InMemoryStatementLogWriter>();
        services.AddScoped<ITelemetrySource, QueryStoreGateway>();
        services.AddScoped<ICapabilityProbe, CapabilityProbe>();

        services.Configure<ProbeOptions>(configuration.GetSection(ProbeOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ProbeOptions>>().Value);
        services.AddScoped<ProbeConnection>();
        services.AddScoped<ProbeConnectionJob>();

        return services;
    }

    /// <summary>
    /// RF-CFG-02: el sondeo debe responder en ~15 s, y con el sondeo de cola por defecto (15 s) un trabajo podía
    /// esperar eso mismo antes de empezar. La cola probe usa su propio almacenamiento, sobre las mismas tablas,
    /// con sondeo de 1 s; las demás colas mantienen 15 s para no cargar PostgreSQL.
    /// </summary>
    public static readonly TimeSpan ProbeQueuePollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Almacenamiento Hangfire para el servidor de la cola probe (mismo esquema, sondeo corto).</summary>
    public static JobStorage CreateProbeQueueStorage(string connectionString) =>
        new PostgreSqlStorage(
            new NpgsqlConnectionFactory(connectionString, ProbeStorageOptions),
            ProbeStorageOptions);

    private static PostgreSqlStorageOptions ProbeStorageOptions => new()
    {
        SchemaName = HangfireSchema,
        PrepareSchemaIfNecessary = false,
        QueuePollInterval = ProbeQueuePollInterval,
    };

    private const string HangfireSchema = "hangfire";

    /// <summary>
    /// Configura el almacenamiento de Hangfire sobre PostgreSQL (ADR-04, DA-03).
    /// </summary>
    public static IServiceCollection AddQueryDoctorHangfire(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetPostgresConnectionString(configuration);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHangfire(config =>
            {
                config.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                      .UseSimpleAssemblyNameTypeSerializer()
                      .UseRecommendedSerializerSettings()
                      .UsePostgreSqlStorage(options =>
                      {
                          options.UseNpgsqlConnection(connectionString);
                      }, new PostgreSqlStorageOptions
                      {
                          SchemaName = HangfireSchema,
                          PrepareSchemaIfNecessary = true,
                          QueuePollInterval = TimeSpan.FromSeconds(15)
                      });
            });
        }

        return services;
    }

    /// <summary>
    /// Obtiene la cadena de conexión hacia PostgreSQL a partir de configuración o secretos (RNF-SEG-04).
    /// </summary>
    public static string? GetPostgresConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetConnectionString("QueryDoctor")
            ?? BuildConnectionStringFromSecret(configuration);
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
