namespace QueryDoctor.Application.Retention;

/// <summary>
/// Configuración de la política de retención de texto SQL y planes (S2-13, RNF-SEG-10).
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";
    public const int DefaultRetentionDays = 30;
    public const string DefaultCron = "15 2 * * *";

    /// <summary>Días que se conservan el texto SQL y los planes XML antes de purgarlos automáticamente.</summary>
    public int DefaultDays { get; set; } = DefaultRetentionDays;

    /// <summary>Expresión cron UTC del trabajo periódico de purga en Hangfire.</summary>
    public string Cron { get; set; } = DefaultCron;

    public void Validate()
    {
        if (DefaultDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultDays), "Retention:DefaultDays debe ser al menos 1.");
        }

        if (string.IsNullOrWhiteSpace(Cron))
        {
            throw new ArgumentException("Retention:Cron no puede estar vacío.", nameof(Cron));
        }
    }
}

/// <summary>Resultado de una corrida automática de purga por retención (solo conteos; RNF-SEG-09).</summary>
public sealed record RetentionPurgeOutcome(int TextsCleared, int PlansCleared);

/// <summary>Resultado del borrado de SQL y planes de una conexión a solicitud de un DBA (RNF-SEG-10).</summary>
public abstract record DeleteConnectionTelemetryOutcome
{
    private DeleteConnectionTelemetryOutcome()
    {
    }

    public sealed record Found(Guid ConnectionId, int TextsCleared, int PlansCleared) : DeleteConnectionTelemetryOutcome;

    public sealed record NotFound : DeleteConnectionTelemetryOutcome;
}

/// <summary>
/// Persistencia de retención y borrado seguro: pone en <see langword="null"/> <c>EncryptedSqlText</c> y
/// <c>EncryptedPlanXml</c> sin eliminar la consulta, las estadísticas, los hallazgos ni las trazas de auditoría.
/// </summary>
public interface IRetentionStore
{
    Task<RetentionPurgeOutcome> PurgeExpiredAsync(DateTimeOffset cutoffUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<DeleteConnectionTelemetryOutcome> DeleteConnectionDataAsync(
        Guid connectionId,
        Guid? actorUserId,
        string? actorUserName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Purga automática de texto SQL y planes expirados al vencer <see cref="RetentionOptions.DefaultDays"/> (S2-13, RNF-SEG-10).
/// </summary>
public sealed class PurgeExpiredTelemetry
{
    private readonly IRetentionStore _store;
    private readonly TimeProvider _clock;
    private readonly RetentionOptions _options;

    public PurgeExpiredTelemetry(IRetentionStore store, TimeProvider clock, RetentionOptions options)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public Task<RetentionPurgeOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow();
        var cutoff = now.AddDays(-_options.DefaultDays);
        return _store.PurgeExpiredAsync(cutoff, now, cancellationToken);
    }
}

/// <summary>
/// Borrado bajo demanda del texto SQL y planes XML de una conexión ejecutado por un DBA (S2-13, RNF-SEG-10, RNF-SEG-12).
/// </summary>
public sealed class DeleteConnectionTelemetry
{
    private readonly IRetentionStore _store;
    private readonly TimeProvider _clock;

    public DeleteConnectionTelemetry(IRetentionStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<DeleteConnectionTelemetryOutcome> ExecuteAsync(
        Guid connectionId,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken cancellationToken = default) =>
        _store.DeleteConnectionDataAsync(connectionId, actorUserId, actorUserName, _clock.GetUtcNow(), cancellationToken);
}
