using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace QueryDoctor.PerfTests;

/// <summary>Parámetros de la medición del tablero (supuestos S1–S6 de S3-07).</summary>
public sealed record DashboardOptions(
    Guid ConnectionId,
    int Requests,
    int Warmup,
    IReadOnlyList<int> Concurrency,
    int Repetitions,
    int MaxSecondsPerScenario,
    int Seed,
    int DetailSample);

/// <summary>Un escenario: qué ruta se pide, con qué meta (null = informativo, sin meta del SRS) y a qué concurrencias.</summary>
public sealed record DashboardScenario(string Name, double? GoalMs, bool OnlyConcurrencyOne, Func<int, string> Path);

/// <summary>Una solicitud medida: latencia desde el envío hasta leer el cuerpo completo, por Nginx.</summary>
public sealed record DashboardSample(int Repetition, int Concurrency, string Scenario, int Index, double OffsetMs, double LatencyMs, int Status, long Bytes);

/// <summary>Resultado de un escenario en una repetición y concurrencia, o de todas las repeticiones juntas (Repetition = 0).</summary>
public sealed record DashboardScenarioResult(
    int Repetition,
    int Concurrency,
    string Scenario,
    double? GoalMs,
    int Requested,
    int Measured,
    int Errors,
    bool StoppedByTime,
    double WallSeconds,
    double RequestsPerSecond,
    LatencySummary All,
    LatencySummary Ok,
    IReadOnlyDictionary<string, int> StatusCounts,
    bool? MeetsGoal);

public sealed record DashboardReport(
    DashboardOptions Options,
    int QueriesInList,
    int DetailIds,
    int PlanIds,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    IReadOnlyList<DashboardScenarioResult> Results,
    IReadOnlyList<DashboardScenarioResult> Pooled);

/// <summary>
/// RNF-RND-02 / CA-HU04.2: latencia del listado (≤ 2 s p95 †) y del detalle sin IA (≤ 3 s p95 †) con 5 000 consultas.
/// Bucle cerrado: C usuarios virtuales piden sin pausa hasta completar N solicitudes medidas (o el tope de tiempo por
/// escenario, que se reporta). El calentamiento se descarta. Solo la Api pública por Nginx; nada se escribe.
/// </summary>
public static class DashboardCommands
{
    public const string Window = "24h";

    /// <summary>
    /// Veredicto de un escenario con meta: cumple si no hubo errores y el p95 de TODAS las solicitudes medidas no pasa
    /// la meta. Una solicitud con error cuenta con el tiempo que tardó en fallar (Nginx corta a los 60 s con 504).
    /// </summary>
    public static bool? Verdict(double? goalMs, int errors, LatencySummary all)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (goalMs is null)
        {
            return null;
        }

        return all.Count > 0 && errors == 0 && all.P95Ms <= goalMs.Value;
    }

    /// <summary>Elige <paramref name="count"/> elementos distintos con una semilla fija (Fisher–Yates parcial).</summary>
    public static IReadOnlyList<T> SampleWithSeed<T>(IReadOnlyList<T> items, int count, int seed)
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        var random = new Random(seed);
        var take = Math.Min(count, copy.Length);
        for (var i = 0; i < take; i++)
        {
            var j = random.Next(i, copy.Length);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy[..take];
    }

    public static async Task<(DashboardReport Report, IReadOnlyList<DashboardSample> Samples)> RunAsync(
        PlatformClient client,
        LabSecrets secrets,
        DashboardOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(options);

        await IngestionCommands.LoginAsDbaAsync(client, secrets, cancellationToken);
        var started = DateTimeOffset.UtcNow;
        var c = options.ConnectionId;

        // Catálogo (sin medir): las consultas del listado y, de una muestra fija de 100, el primer plan de cada una.
        var list = await client.GetJsonAsync<JsonElement>($"queries?connectionId={c}&window={Window}&sort=score", cancellationToken);
        var allIds = list.EnumerateArray().Select(q => q.GetProperty("id").GetGuid()).ToList();
        var detailIds = SampleWithSeed(allIds, options.DetailSample, options.Seed);
        if (detailIds.Count == 0)
        {
            throw new InvalidOperationException("El listado no devolvió consultas: no hay nada que medir.");
        }

        var planTargets = new List<(Guid Query, Guid Plan)>();
        foreach (var id in detailIds)
        {
            var detail = await client.GetJsonAsync<JsonElement>($"queries/{id}?window={Window}", cancellationToken);
            if (detail.TryGetProperty("plans", out var plans) && plans.GetArrayLength() > 0)
            {
                planTargets.Add((id, plans[0].GetProperty("planId").GetGuid()));
            }
        }

        var scenarios = new List<DashboardScenario>
        {
            new("listado_score", 2000, false, _ => $"queries?connectionId={c}&window={Window}&sort=score"),
            new("detalle", 3000, false, i => $"queries/{detailIds[i % detailIds.Count]}?window={Window}"),
            new("listado_consumo", null, true, _ => $"queries?connectionId={c}&window={Window}&sort=consumption"),
        };
        if (planTargets.Count > 0)
        {
            scenarios.Add(new("arbol_plan", null, true, i =>
            {
                var t = planTargets[i % planTargets.Count];
                return $"queries/{t.Query}/plans/{t.Plan}";
            }));
        }

        var results = new List<DashboardScenarioResult>();
        var samples = new List<DashboardSample>();
        for (var rep = 1; rep <= options.Repetitions; rep++)
        {
            foreach (var concurrency in options.Concurrency)
            {
                foreach (var scenario in scenarios.Where(s => !s.OnlyConcurrencyOne || concurrency == 1))
                {
                    var (result, measured) = await RunScenarioAsync(client, scenario, rep, concurrency, options, cancellationToken);
                    results.Add(result);
                    samples.AddRange(measured);
                    Console.WriteLine(
                        $"  r{rep} c={concurrency} {scenario.Name,-16} n={result.Measured,4} err={result.Errors,3} " +
                        $"p50={result.All.P50Ms,8:0.0} p95={result.All.P95Ms,8:0.0} p99={result.All.P99Ms,8:0.0} ms" +
                        (result.StoppedByTime ? "  (tope de tiempo)" : string.Empty) +
                        (result.MeetsGoal is { } ok ? (ok ? "  cumple" : "  NO cumple") : "  informativo"));
                }
            }
        }

        var pooled = results
            .GroupBy(r => (r.Concurrency, r.Scenario))
            .Select(g =>
            {
                var groupSamples = samples.Where(s => s.Concurrency == g.Key.Concurrency && s.Scenario == g.Key.Scenario).ToList();
                return Build(0, g.Key.Concurrency, g.Key.Scenario, g.First().GoalMs, g.Sum(r => r.Requested), groupSamples,
                    g.Any(r => r.StoppedByTime), g.Sum(r => r.WallSeconds));
            })
            .ToList();

        var report = new DashboardReport(options, allIds.Count, detailIds.Count, planTargets.Count, started, DateTimeOffset.UtcNow, results, pooled);
        return (report, samples);
    }

    private static async Task<(DashboardScenarioResult Result, List<DashboardSample> Samples)> RunScenarioAsync(
        PlatformClient client,
        DashboardScenario scenario,
        int repetition,
        int concurrency,
        DashboardOptions options,
        CancellationToken cancellationToken)
    {
        // Calentamiento a la misma concurrencia; sus solicitudes no se guardan.
        _ = await RunLoopAsync(client, scenario, repetition, concurrency, options.Warmup, TimeSpan.FromSeconds(options.MaxSecondsPerScenario), cancellationToken);

        var wall = Stopwatch.StartNew();
        var (measured, stoppedByTime) = await RunLoopAsync(
            client, scenario, repetition, concurrency, options.Requests, TimeSpan.FromSeconds(options.MaxSecondsPerScenario), cancellationToken);
        wall.Stop();

        return (Build(repetition, concurrency, scenario.Name, scenario.GoalMs, options.Requests, measured, stoppedByTime, wall.Elapsed.TotalSeconds), measured);
    }

    private static async Task<(List<DashboardSample> Samples, bool StoppedByTime)> RunLoopAsync(
        PlatformClient client,
        DashboardScenario scenario,
        int repetition,
        int concurrency,
        int requests,
        TimeSpan maxDuration,
        CancellationToken cancellationToken)
    {
        var samples = new List<DashboardSample>(requests);
        var gate = new object();
        var next = -1;
        var stoppedByTime = false;
        var clock = Stopwatch.StartNew();

        async Task WorkerAsync()
        {
            while (true)
            {
                if (clock.Elapsed > maxDuration)
                {
                    stoppedByTime = true;
                    return;
                }

                var index = Interlocked.Increment(ref next);
                if (index >= requests)
                {
                    return;
                }

                var offset = clock.Elapsed.TotalMilliseconds;
                var t0 = Stopwatch.GetTimestamp();
                int status;
                long bytes = 0;
                try
                {
                    using var response = await client.SendAsync(HttpMethod.Get, scenario.Path(index), null, cancellationToken);
                    status = (int)response.StatusCode;
                    bytes = response.Content.Headers.ContentLength ?? (await response.Content.ReadAsByteArrayAsync(cancellationToken)).LongLength;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    // Tiempo límite del cliente o conexión cortada: cuenta como error con el tiempo que tardó.
                    status = -1;
                }

                var latency = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                lock (gate)
                {
                    samples.Add(new DashboardSample(repetition, concurrency, scenario.Name, index, offset, latency, status, bytes));
                }
            }
        }

        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ => WorkerAsync()));
        return (samples.OrderBy(s => s.Index).ToList(), stoppedByTime);
    }

    private static DashboardScenarioResult Build(
        int repetition, int concurrency, string scenario, double? goalMs, int requested, List<DashboardSample> samples, bool stoppedByTime, double wallSeconds)
    {
        var errors = samples.Count(s => s.Status != (int)HttpStatusCode.OK);
        var all = LatencyStats.Summarize(samples.Select(s => s.LatencyMs));
        var ok = LatencyStats.Summarize(samples.Where(s => s.Status == (int)HttpStatusCode.OK).Select(s => s.LatencyMs));
        var statuses = samples
            .GroupBy(s => s.Status.ToString(CultureInfo.InvariantCulture))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var rps = wallSeconds > 0 ? samples.Count / wallSeconds : 0;
        return new DashboardScenarioResult(
            repetition, concurrency, scenario, goalMs, requested, samples.Count, errors, stoppedByTime, wallSeconds, rps, all, ok, statuses,
            Verdict(goalMs, errors, all));
    }

    /// <summary>CSV crudo, una fila por solicitud medida, con punto decimal.</summary>
    public static async Task WriteSamplesCsvAsync(string path, IReadOnlyList<DashboardSample> samples, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var sb = new StringBuilder("repeticion,concurrencia,escenario,indice,inicio_ms,latencia_ms,http,bytes\n");
        foreach (var s in samples)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{s.Repetition},{s.Concurrency},{s.Scenario},{s.Index},{s.OffsetMs:0.###},{s.LatencyMs:0.###},{s.Status},{s.Bytes}\n");
        }

        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    /// <summary>CSV de resumen: una fila por (repetición, concurrencia, escenario) y las filas "todas" (repetición 0).</summary>
    public static async Task WriteSummaryCsvAsync(string path, DashboardReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder(
            "repeticion,concurrencia,escenario,meta_ms,pedidas,medidas,errores,tope_tiempo,seg_reloj,sol_por_s," +
            "min_ms,p50_ms,p95_ms,p99_ms,max_ms,media_ms,de_ms,cumple\n");
        foreach (var r in report.Results.Concat(report.Pooled))
        {
            var rep = r.Repetition == 0 ? "todas" : r.Repetition.ToString(CultureInfo.InvariantCulture);
            var verdict = r.MeetsGoal switch { true => "si", false => "no", null => "informativo" };
            sb.Append(CultureInfo.InvariantCulture,
                $"{rep},{r.Concurrency},{r.Scenario},{r.GoalMs},{r.Requested},{r.Measured},{r.Errors},{(r.StoppedByTime ? "si" : "no")}," +
                $"{r.WallSeconds:0.###},{r.RequestsPerSecond:0.###},{r.All.MinMs:0.###},{r.All.P50Ms:0.###},{r.All.P95Ms:0.###}," +
                $"{r.All.P99Ms:0.###},{r.All.MaxMs:0.###},{r.All.MeanMs:0.###},{r.All.StdDevMs:0.###},{verdict}\n");
        }

        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false), cancellationToken);
    }
}
