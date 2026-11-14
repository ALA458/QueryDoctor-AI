using QueryDoctor.Application.Connections;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Ingestion;

/// <summary>Resultado de pedir una ingesta manual.</summary>
public abstract record RequestIngestionResult
{
    private RequestIngestionResult()
    {
    }

    /// <summary>Encolada en la cola ingest (202).</summary>
    public sealed record Accepted(Guid ConnectionId) : RequestIngestionResult;

    public sealed record NotFound : RequestIngestionResult;

    /// <summary>La conexión existe pero no está Active (409).</summary>
    public sealed record NotActive(ConnectionStatus Status) : RequestIngestionResult;

    /// <summary>Ya hay una ingesta de la conexión en curso o en cola (409): no se encola otra (auditoría B1).</summary>
    public sealed record AlreadyInProgress : RequestIngestionResult;
}

/// <summary>
/// Ingesta manual pedida por un DBA (RF-ING-06, CA-HU03.1). Corre en la Api: solo valida y encola; la ingesta corre
/// en el Worker (invariante 2). El argumento del trabajo es solo el id (§8.1).
/// </summary>
public sealed class RequestIngestion
{
    private readonly IConnectionRepository _connections;
    private readonly IIngestionQueries _runs;
    private readonly IIngestionJobScheduler _scheduler;

    public RequestIngestion(IConnectionRepository connections, IIngestionQueries runs, IIngestionJobScheduler scheduler)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    public async Task<RequestIngestionResult> ExecuteAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await _connections.GetAsync(connectionId, cancellationToken);
        if (connection is null)
        {
            return new RequestIngestionResult.NotFound();
        }

        if (connection.Status != ConnectionStatus.Active)
        {
            return new RequestIngestionResult.NotActive(connection.Status);
        }

        // Auditoría B1 (RNF-RND-01): POST repetidos no deben encolar lecturas repetidas de la base objetivo. Dos POST
        // simultáneos aún podrían encolar dos trabajos; el bloqueo por conexión los serializa y el segundo no duplica.
        if (await _runs.HasRunningAsync(connectionId, cancellationToken) || !_scheduler.TryEnqueueManual(connectionId))
        {
            return new RequestIngestionResult.AlreadyInProgress();
        }

        return new RequestIngestionResult.Accepted(connectionId);
    }
}

/// <summary>Una ejecución de ingesta para GET /connections/{id}/ingestions: causa como código y mensaje fijo.</summary>
public sealed record IngestionRunView(
    Guid Id,
    IngestionTrigger Trigger,
    IngestionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int? RowsUpserted,
    IngestionCause? Cause,
    string? CauseMessage)
{
    public static IngestionRunView From(IngestionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        IngestionCause? cause = Enum.TryParse<IngestionCause>(run.ErrorCause, out var parsed) ? parsed : null;
        if (cause is null && !string.IsNullOrEmpty(run.ErrorCause))
        {
            cause = IngestionCause.Unknown;
        }

        return new IngestionRunView(
            run.Id,
            run.Trigger,
            run.Status,
            run.StartedAt,
            run.FinishedAt,
            run.RowsUpserted,
            cause,
            cause is null ? null : IngestionCauseMessages.For(cause.Value));
    }
}

/// <summary>Lecturas de ejecuciones de ingesta (la Api solo lee PostgreSQL).</summary>
public interface IIngestionQueries
{
    /// <summary>null si la conexión no existe; si existe, sus últimas ejecuciones, la más reciente primero.</summary>
    Task<IReadOnlyList<IngestionRunView>?> ListRunsAsync(Guid connectionId, int take, CancellationToken cancellationToken);

    /// <summary>Hay una ejecución en Running para la conexión.</summary>
    Task<bool> HasRunningAsync(Guid connectionId, CancellationToken cancellationToken);
}
