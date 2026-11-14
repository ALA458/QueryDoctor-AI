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
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Infrastructure.Connections;
using QueryDoctor.Infrastructure.Ingestion;
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

        // Motor determinístico: parser de Showplan XML en memoria sin E/S (S1-12, RF-MOT-01, RF-MOT-16)
        services.TryAddSingleton<PlanParserOptions>();
        services.TryAddSingleton<IPlanParser, ShowplanXmlParser>();

        // Motor determinístico: parser de Showplan XML, catálogo versionado y motor de reglas sin E/S (S1-12, S1-13, RF-MOT-01..17)
        services.TryAddSingleton<PlanParserOptions>();
        services.TryAddSingleton<IPlanParser, ShowplanXmlParser>();
        services.TryAddSingleton(_ => BindRuleThresholds(configuration));
        services.TryAddSingleton<IRuleCatalog, VersionedRuleCatalog>();
        services.TryAddSingleton<IRuleEngine, DeterministicRuleEngine>();

        // Motor determinístico: parser de Showplan XML, catálogo versionado y motor de reglas sin E/S (S1-12, S1-13, S1-14, RF-MOT-01..17, RNF-MAN-01)
        services.TryAddSingleton<PlanParserOptions>();
        services.TryAddSingleton<IPlanParser, ShowplanXmlParser>();
        services.TryAddSingleton(_ => BindRuleThresholds(configuration));
        foreach (var builtInRule in VersionedRuleCatalog.DiscoverBuiltInRules())
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IRule>(builtInRule));
        }

        services.TryAddSingleton<IRuleCatalog, VersionedRuleCatalog>();
        services.TryAddSingleton<IRuleEngine, DeterministicRuleEngine>();

        // Telemetría e ingesta de SQL Server (S1-08, RNF-SEG-01, RF-TRZ-04)
        services.TryAddSingleton<IStatementLogWriter, InMemoryStatementLogWriter>();
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
            services.AddScoped<IIngestionQueries, EfIngestionQueries>();
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
    /// Ingesta manual para la Api (S1-11): valida y encola ingest_connection. No registra el Gateway ni el lector de
    /// Query Store: la Api nunca se conecta a bases objetivo (invariante 2).
    /// </summary>
    public static IServiceCollection AddIngestionRequests(this IServiceCollection services)
    {
        services.AddScoped<IIngestionJobScheduler, HangfireIngestionJobScheduler>();
        services.AddScoped<RequestIngestion>();
        return services;
    }

    /// <summary>
    /// Ingesta incremental: SOLO para el Worker, junto con <see cref="AddTargetTelemetry"/> (S1-11, §6.2). Necesita
    /// PostgreSQL: sin cadena de conexión no se registra (igual que la persistencia).
    /// </summary>
    public static IServiceCollection AddIngestion(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = GetPostgresConnectionString(configuration);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        services.Configure<IngestionOptions>(configuration.GetSection(IngestionOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
            options.Validate();
            return options;
        });

        services.AddSingleton<IIngestionLock>(_ => new PostgresIngestionLock(connectionString));
        services.AddScoped<IQueryStoreReader, QueryStoreReader>();
        services.AddScoped<IIngestionStore, EfIngestionStore>();
        services.AddScoped<IngestConnection>();
        services.AddScoped<IngestConnectionJob>();

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

    private static RuleThresholds BindRuleThresholds(IConfiguration configuration)
    {
        var section = configuration.GetSection(RuleThresholds.SectionName);
        if (!section.Exists())
        {
            return RuleThresholds.Default;
        }

        return new RuleThresholds
        {
            Reg03MinEstimatedRows = ReadDouble(section, nameof(RuleThresholds.Reg03MinEstimatedRows), RuleThresholds.DefaultReg03MinEstimatedRows),
            Reg03RowsReadToReturnedRatio = ReadDouble(section, nameof(RuleThresholds.Reg03RowsReadToReturnedRatio), RuleThresholds.DefaultReg03RowsReadToReturnedRatio),
            MinQueryConsumptionPct = ReadDouble(section, nameof(RuleThresholds.MinQueryConsumptionPct), RuleThresholds.DefaultMinQueryConsumptionPct),
            Reg04MinEstimatedExecutions = ReadDouble(section, nameof(RuleThresholds.Reg04MinEstimatedExecutions), RuleThresholds.DefaultReg04MinEstimatedExecutions),
            Reg07CardinalityRatio = ReadDouble(section, nameof(RuleThresholds.Reg07CardinalityRatio), RuleThresholds.DefaultReg07CardinalityRatio),
            Reg08RegressionIncreasePct = ReadDouble(section, nameof(RuleThresholds.Reg08RegressionIncreasePct), RuleThresholds.DefaultReg08RegressionIncreasePct),
            Reg08MinExecutionsPerPlan = ReadLong(section, nameof(RuleThresholds.Reg08MinExecutionsPerPlan), RuleThresholds.DefaultReg08MinExecutionsPerPlan),
            Reg10DurationMaxMinRatio = ReadDouble(section, nameof(RuleThresholds.Reg10DurationMaxMinRatio), RuleThresholds.DefaultReg10DurationMaxMinRatio),
        };
    }

    private static double ReadDouble(IConfigurationSection section, string key, double fallback) =>
        double.TryParse(section[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static long ReadLong(IConfigurationSection section, string key, long fallback) =>
        long.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
