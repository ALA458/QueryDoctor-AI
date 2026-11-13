using QueryDoctor.Application.Connections;
using QueryDoctor.Application.Ingestion;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Ingestion;

internal sealed class FakeQueryStoreReader : IQueryStoreReader
{
    public QueryStoreOptions? Options { get; set; } = new("READ_WRITE", "READ_WRITE", null, 1, "ALL", 30);

    public List<QueryStoreStatRow> Rows { get; } = [];

    public int Discarded { get; set; }

    public Dictionary<long, string?> Texts { get; } = [];

    public Dictionary<long, string?> Plans { get; } = [];

    public Exception? FailOptions { get; set; }

    public Exception? FailStats { get; set; }

    public List<QueryStoreReadRequest> Requests { get; } = [];

    public List<long> TextReads { get; } = [];

    public List<long> PlanReads { get; } = [];

    public List<TargetDatabaseConnection> Targets { get; } = [];

    public Task<QueryStoreOptions?> GetOptionsAsync(TargetDatabaseConnection target, CancellationToken cancellationToken)
    {
        Targets.Add(target);
        return FailOptions is null ? Task.FromResult(Options) : Task.FromException<QueryStoreOptions?>(FailOptions);
    }

    public Task<QueryStoreReadResult> ReadStatsAsync(TargetDatabaseConnection target, QueryStoreReadRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return FailStats is null
            ? Task.FromResult(new QueryStoreReadResult(Rows.ToList(), Discarded))
            : Task.FromException<QueryStoreReadResult>(FailStats);
    }

    public Task<QueryStoreDocument> GetQueryTextAsync(TargetDatabaseConnection target, long queryStoreTextId, CancellationToken cancellationToken)
    {
        TextReads.Add(queryStoreTextId);
        return Task.FromResult(Texts.TryGetValue(queryStoreTextId, out var t) ? new QueryStoreDocument(true, t) : new QueryStoreDocument(false, null));
    }

    public Task<QueryStoreDocument> GetPlanXmlAsync(TargetDatabaseConnection target, long queryStorePlanId, CancellationToken cancellationToken)
    {
        PlanReads.Add(queryStorePlanId);
        return Task.FromResult(Plans.TryGetValue(queryStorePlanId, out var p) ? new QueryStoreDocument(true, p) : new QueryStoreDocument(false, null));
    }
}

internal sealed class FakeIngestionLock : IIngestionLock
{
    public bool Busy { get; set; }

    public int Released { get; private set; }

    public Task<IAsyncDisposable?> TryAcquireAsync(Guid connectionId, CancellationToken cancellationToken) =>
        Task.FromResult<IAsyncDisposable?>(Busy ? null : new Lease(this));

    private sealed class Lease(FakeIngestionLock owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Released++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class InMemoryIngestionStore : IIngestionStore
{
    public Connection? Connection { get; set; }

    public CapabilityProfile? Profile { get; set; }

    public List<IngestionRun> Runs { get; } = [];

    public List<IngestionBatch> Batches { get; } = [];

    public HashSet<long> KnownTexts { get; } = [];

    public HashSet<long> KnownPlans { get; } = [];

    public Exception? FailApply { get; set; }

    public int InterruptedClosed { get; private set; }

    public Task<Connection?> GetConnectionAsync(Guid connectionId, CancellationToken cancellationToken) =>
        Task.FromResult(Connection?.Id == connectionId ? Connection : null);

    public Task<CapabilityProfile?> GetProfileAsync(Guid connectionId, CancellationToken cancellationToken) => Task.FromResult(Profile);

    public Task<int> CloseInterruptedRunsAsync(Guid connectionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var running = Runs.Where(r => r.ConnectionId == connectionId && r.Status == IngestionStatus.Running).ToList();
        foreach (var run in running)
        {
            run.Status = IngestionStatus.Failed;
            run.ErrorCause = nameof(IngestionCause.Interrupted);
            run.FinishedAt = now;
        }

        InterruptedClosed += running.Count;
        return Task.FromResult(running.Count);
    }

    public Task<Guid> StartRunAsync(Guid connectionId, IngestionTrigger trigger, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = new IngestionRun { Id = Guid.NewGuid(), ConnectionId = connectionId, Trigger = trigger, StartedAt = now };
        Runs.Add(run);
        return Task.FromResult(run.Id);
    }

    public Task FinishRunAsync(Guid runId, IngestionStatus status, IngestionCause cause, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var run = Runs.Single(r => r.Id == runId);
        run.Status = status;
        run.ErrorCause = cause.ToString();
        run.FinishedAt = now;
        return Task.CompletedTask;
    }

    public Task RecordSkippedAsync(Guid connectionId, IngestionTrigger trigger, IngestionCause cause, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Runs.Add(new IngestionRun
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            Trigger = trigger,
            Status = IngestionStatus.Skipped,
            ErrorCause = cause.ToString(),
            StartedAt = now,
            FinishedAt = now,
        });
        return Task.CompletedTask;
    }

    public Task<KnownQueryStoreIds> GetKnownIdsAsync(Guid connectionId, IReadOnlyCollection<long> textIds, IReadOnlyCollection<long> planIds, CancellationToken cancellationToken) =>
        Task.FromResult(new KnownQueryStoreIds(KnownTexts.Intersect(textIds).ToHashSet(), KnownPlans.Intersect(planIds).ToHashSet()));

    public Task<int> ApplyAsync(IngestionBatch batch, CancellationToken cancellationToken)
    {
        if (FailApply is not null)
        {
            return Task.FromException<int>(FailApply);
        }

        Batches.Add(batch);
        KnownTexts.UnionWith(batch.Texts.Select(t => t.QueryStoreTextId));
        KnownPlans.UnionWith(batch.Plans.Select(p => p.QueryStorePlanId));
        Connection!.LastConsolidatedIntervalId = batch.NewWatermark;

        var run = Runs.Single(r => r.Id == batch.RunId);
        run.Status = IngestionStatus.Succeeded;
        run.FinishedAt = batch.Now;
        run.RowsUpserted = batch.Stats.Count;
        return Task.FromResult(batch.Stats.Count);
    }
}
