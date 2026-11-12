using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Analysis;
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

        // Motor determinístico: parser de Showplan XML, catálogo versionado y motor de reglas sin E/S (S1-12, S1-13, RF-MOT-01..17)
        services.TryAddSingleton<PlanParserOptions>();
        services.TryAddSingleton<IPlanParser, ShowplanXmlParser>();
        services.TryAddSingleton(_ => BindRuleThresholds(configuration));
        services.TryAddSingleton<IRuleCatalog, VersionedRuleCatalog>();
        services.TryAddSingleton<IRuleEngine, DeterministicRuleEngine>();

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
