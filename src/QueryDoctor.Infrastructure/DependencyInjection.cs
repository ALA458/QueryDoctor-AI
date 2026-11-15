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
using QueryDoctor.Application.Ai;
using QueryDoctor.Application.Common.Security;
using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Engine.Parsing;
using QueryDoctor.Application.Engine.Rules;
using QueryDoctor.Application.Explanations;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.KnowledgeBase;
using QueryDoctor.Application.Masking;
using QueryDoctor.Application.Scoring;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Application.Validation;
using QueryDoctor.Domain.Analysis;
using QueryDoctor.Infrastructure.Ai;
using QueryDoctor.Infrastructure.Connections;
using QueryDoctor.Infrastructure.Explanations;
using QueryDoctor.Infrastructure.Ingestion;
using QueryDoctor.Infrastructure.Persistence;
using QueryDoctor.Infrastructure.Scoring;
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

        // Motor determinístico: parser de Showplan XML, catálogo versionado y motor de reglas sin E/S (S1-12, S1-13, S1-14, S2-01, RF-MOT-01..17, RNF-MAN-01)
        services.TryAddSingleton<PlanParserOptions>();
        services.TryAddSingleton<IPlanParser, ShowplanXmlParser>();
        services.TryAddSingleton(_ => BindRuleThresholds(configuration));
        foreach (var builtInRule in VersionedRuleCatalog.DiscoverBuiltInRules())
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IRule>(builtInRule));
        }

        services.TryAddSingleton<IRuleCatalog, VersionedRuleCatalog>();
        services.TryAddSingleton<IRuleEngine, DeterministicRuleEngine>();

        // Calculadora pura del puntaje compuesto de salud H(q) (S2-02, RF-SCO-01..06, Invariante 7)
        services.TryAddSingleton(_ => BindHealthScoreParameters(configuration));
        services.TryAddSingleton<IHealthScoreCalculator, HealthScoreCalculator>();

        // Validador determinista puro de explicaciones de IA (S2-03, RF-IA-03, RF-IA-10, RF-IA-12, RNF-FUN-02)
        services.TryAddSingleton(_ => BindExplanationValidationOptions(configuration));
        services.TryAddSingleton<IExplanationValidator, ExplanationValidator>();

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
            services.AddScoped<IKnowledgeBaseRepository, EfKnowledgeBaseRepository>();
            services.AddScoped<IExplanationValidationContextBuilder, ExplanationValidationContextBuilder>();
            services.AddScoped<IScoreRecalculationStore, EfScoreRecalculationStore>();
            services.AddScoped<RecalculateConnectionScores>();
            services.AddScoped<RecalculateScoresJob>();
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
        services.TryAddScoped<IScoreRecalculationStore, EfScoreRecalculationStore>();
        services.TryAddScoped<RecalculateConnectionScores>();
        services.TryAddScoped<RecalculateScoresJob>();
        services.AddScoped<IngestConnection>();
        services.AddScoped<IngestConnectionJob>();

        return services;
    }

    /// <summary>
    /// Enmascarador de literales (S2-04, RNF-SEG-05): SOLO para el Worker, que es quien arma el contexto hacia la
    /// IA (invariante 2). No recibe configuración a propósito: no existe clave que lo desactive (CA-HU21.4).
    /// </summary>
    public static IServiceCollection AddLiteralMasking(this IServiceCollection services)
    {
        services.AddSingleton<IMasker, LiteralMasker>();
        return services;
    }

    /// <summary>
    /// Solicitud y consulta de explicaciones (S2-05): para la Api. Solo guarda y encola explain con el id; no registra
    /// el cliente de IA ni el enmascarador (invariante 2).
    /// </summary>
    public static IServiceCollection AddExplanationRequests(this IServiceCollection services)
    {
        services.TryAddScoped<EfExplanationStore>();
        services.TryAddScoped<IExplanationStore>(sp => sp.GetRequiredService<EfExplanationStore>());
        services.AddScoped<IExplanationJobScheduler, HangfireExplanationJobScheduler>();
        services.AddScoped<RequestExplanation>();
        services.AddScoped<GetLatestExplanation>();
        return services;
    }

    /// <summary>
    /// Trabajo explain (S2-05): SOLO para el Worker, que es el único que habla con el servicio de IA (invariante 2).
    /// Necesita <see cref="AddLiteralMasking"/> y PostgreSQL; sin cadena de conexión no se registra.
    /// </summary>
    public static IServiceCollection AddExplanations(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (string.IsNullOrWhiteSpace(GetPostgresConnectionString(configuration)))
        {
            return services;
        }

        services.Configure<AiServiceOptions>(configuration.GetSection(AiServiceOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AiServiceOptions>>().Value;
            options.Validate();
            return options;
        });

        services.AddSingleton<IAiExplanationClient>(sp => new HttpAiExplanationClient(
            HttpAiExplanationClient.CreateHttpClient(),
            sp.GetRequiredService<AiServiceOptions>()));
        services.AddSingleton<ExplanationContextBuilder>();
        services.TryAddScoped<EfExplanationStore>();
        services.TryAddScoped<IExplanationStore>(sp => sp.GetRequiredService<EfExplanationStore>());
        services.AddScoped<IExplanationSourceReader>(sp => sp.GetRequiredService<EfExplanationStore>());
        services.AddScoped<ExplainQuery>();
        services.AddScoped<ExplainQueryJob>();

        return services;
    }

    /// <summary>
    /// E2, CA-HU09.3: una explicación en caché debe verse en ≤ 2 s, y con el sondeo de 15 s el trabajo esperaría
    /// más que eso antes de empezar. La cola explain tiene su propio servidor con el mismo sondeo de 1 s que probe.
    /// </summary>
    public static JobStorage CreateExplainQueueStorage(string connectionString) => CreateProbeQueueStorage(connectionString);

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

    private static HealthScoreParameters BindHealthScoreParameters(IConfiguration configuration)
    {
        var section = configuration.GetSection(HealthScoreParameters.SectionName);
        if (!section.Exists())
        {
            return HealthScoreParameters.Default;
        }

        var parameters = new HealthScoreParameters
        {
            CpuWeight = ReadDouble(section, nameof(HealthScoreParameters.CpuWeight), HealthScoreParameters.DefaultCpuWeight),
            DurationWeight = ReadDouble(section, nameof(HealthScoreParameters.DurationWeight), HealthScoreParameters.DefaultDurationWeight),
            LogicalReadsWeight = ReadDouble(section, nameof(HealthScoreParameters.LogicalReadsWeight), HealthScoreParameters.DefaultLogicalReadsWeight),
            SaturationCapTau = ReadDouble(section, nameof(HealthScoreParameters.SaturationCapTau), HealthScoreParameters.DefaultSaturationCapTau),
            BasePriorityAlpha = ReadDouble(section, nameof(HealthScoreParameters.BasePriorityAlpha), HealthScoreParameters.DefaultBasePriorityAlpha),
            LowSeverityWeight = ReadDouble(section, nameof(HealthScoreParameters.LowSeverityWeight), HealthScoreParameters.DefaultLowSeverityWeight),
            MediumSeverityWeight = ReadDouble(section, nameof(HealthScoreParameters.MediumSeverityWeight), HealthScoreParameters.DefaultMediumSeverityWeight),
            HighSeverityWeight = ReadDouble(section, nameof(HealthScoreParameters.HighSeverityWeight), HealthScoreParameters.DefaultHighSeverityWeight),
            EstimatedConfidenceWeight = ReadDouble(section, nameof(HealthScoreParameters.EstimatedConfidenceWeight), HealthScoreParameters.DefaultEstimatedConfidenceWeight),
            AggregatedExecutionConfidenceWeight = ReadDouble(section, nameof(HealthScoreParameters.AggregatedExecutionConfidenceWeight), HealthScoreParameters.DefaultAggregatedExecutionConfidenceWeight),
            ActualPlanConfidenceWeight = ReadDouble(section, nameof(HealthScoreParameters.ActualPlanConfidenceWeight), HealthScoreParameters.DefaultActualPlanConfidenceWeight),
            CriticalUpperExclusive = ReadDouble(section, nameof(HealthScoreParameters.CriticalUpperExclusive), HealthScoreParameters.DefaultCriticalUpperExclusive),
            AttentionUpperExclusive = ReadDouble(section, nameof(HealthScoreParameters.AttentionUpperExclusive), HealthScoreParameters.DefaultAttentionUpperExclusive),
        };

        parameters.Validate();
        return parameters;
    }

    private static ExplanationValidationOptions BindExplanationValidationOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection(ExplanationValidationOptions.SectionName);
        if (!section.Exists())
        {
            return ExplanationValidationOptions.Default;
        }

        var options = new ExplanationValidationOptions
        {
            RelativeTolerance = ReadDouble(
                section,
                nameof(ExplanationValidationOptions.RelativeTolerance),
                ExplanationValidationOptions.DefaultRelativeTolerance),
            AbsoluteEpsilon = ReadDouble(
                section,
                nameof(ExplanationValidationOptions.AbsoluteEpsilon),
                ExplanationValidationOptions.DefaultAbsoluteEpsilon),
            AllowHalfLastDecimalStepRounding = ReadBool(
                section,
                nameof(ExplanationValidationOptions.AllowHalfLastDecimalStepRounding),
                true),
            AllowPercentageScaleEquivalence = ReadBool(
                section,
                nameof(ExplanationValidationOptions.AllowPercentageScaleEquivalence),
                true),
            ValidateSummaryNumbers = ReadBool(
                section,
                nameof(ExplanationValidationOptions.ValidateSummaryNumbers),
                true),
            RequireAllCitedRulesForCausalRelation = ReadBool(
                section,
                nameof(ExplanationValidationOptions.RequireAllCitedRulesForCausalRelation),
                true),
            MaxJsonDepth = ReadInt(
                section,
                nameof(ExplanationValidationOptions.MaxJsonDepth),
                ExplanationValidationOptions.DefaultMaxJsonDepth),
        };

        options.Validate();
        return options;
    }

    private static bool ReadBool(IConfigurationSection section, string key, bool fallback) =>
        bool.TryParse(section[key], out var parsed)
            ? parsed
            : fallback;

    private static int ReadInt(IConfigurationSection section, string key, int fallback) =>
        int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static double ReadDouble(IConfigurationSection section, string key, double fallback) =>
        double.TryParse(section[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static long ReadLong(IConfigurationSection section, string key, long fallback) =>
        long.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
