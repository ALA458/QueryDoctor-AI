using System.Diagnostics;
using System.Text.Json;

namespace QueryDoctor.PerfTests;

/// <summary>Resultado de <c>setup</c>.</summary>
public sealed record SetupResult(Guid ConnectionId, string Status, DateTimeOffset ActiveAtUtc);

/// <summary>
/// Resultado de <c>ingest</c>: la ejecución manual pedida, cualquier otra que apareciera mientras tanto y todas las de la
/// conexión (Run-Perf.ps1 anula la repetición si encuentra una ingesta programada en medio).
/// </summary>
public sealed record IngestResult(
    Guid ConnectionId,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ObservedEndUtc,
    double ObservedSeconds,
    IngestionRunDto Run,
    IReadOnlyList<IngestionRunDto> OtherNewRuns,
    IReadOnlyList<IngestionRunDto> AllRuns);

/// <summary>
/// Comandos de la medición de ingesta (S3-07, RNF-RND-01). La Api solo encola; la ingesta corre en el Worker
/// (invariante 2). Estos comandos no tocan SQL Server ni PostgreSQL: solo la Api pública.
/// </summary>
public static class IngestionCommands
{
    public const string DbaUser = "dba";
    public const string LabServer = "sqlserver";
    public const string LabDatabase = "QD_Lab";
    public const string LabReader = "qd_reader";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly JsonSerializerOptions IndentedJson = new(PlatformClient.Json) { WriteIndented = true };

    /// <summary>
    /// Base de PostgreSQL recién creada: primer ingreso del DBA con cambio obligatorio de contraseña, alta de QD_Lab y
    /// espera del sondeo hasta Active. Si el DBA ya tenía la contraseña de la medición, entra con ella.
    /// </summary>
    public static async Task<SetupResult> SetupAsync(PlatformClient client, LabSecrets secrets, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(secrets);

        await LoginAsDbaAsync(client, secrets, cancellationToken);

        var id = await client.RegisterConnectionAsync(LabServer, LabDatabase, LabReader, secrets.Reader(), cancellationToken);
        var status = await UntilAsync(
            () => client.GetConnectionStatusAsync(id, cancellationToken),
            s => s != "Pending",
            timeout,
            "el sondeo de QD_Lab",
            cancellationToken);

        if (status != "Active")
        {
            throw new InvalidOperationException($"QD_Lab quedó en estado {status} tras el sondeo; se necesita Active para medir.");
        }

        return new SetupResult(id, status, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Pide una ingesta manual y espera a que su ejecución deje de estar en Running. La duración oficial es la de la
    /// ejecución (StartedAt → FinishedAt, marcas del Worker); el tiempo observado desde el POST es informativo.
    /// </summary>
    public static async Task<IngestResult> IngestAsync(PlatformClient client, LabSecrets secrets, Guid connectionId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(secrets);

        await LoginAsDbaAsync(client, secrets, cancellationToken);

        var before = (await client.ListIngestionRunsAsync(connectionId, cancellationToken)).Select(r => r.Id).ToHashSet();
        var requestedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        await client.RequestIngestionAsync(connectionId, cancellationToken);

        var runs = await UntilAsync(
            () => client.ListIngestionRunsAsync(connectionId, cancellationToken),
            list => FindManualRun(list, before) is { Status: not "Running" },
            timeout,
            "la ingesta manual",
            cancellationToken);
        stopwatch.Stop();

        var run = FindManualRun(runs, before)!;
        var others = runs.Where(r => !before.Contains(r.Id) && r.Id != run.Id).ToList();
        return new IngestResult(connectionId, requestedAt, DateTimeOffset.UtcNow, stopwatch.Elapsed.TotalSeconds, run, others, runs);
    }

    /// <summary>La ejecución manual nueva (no estaba antes del POST); la más reciente si hubiera varias.</summary>
    public static IngestionRunDto? FindManualRun(IReadOnlyList<IngestionRunDto> runs, IReadOnlySet<Guid> before)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(before);

        return runs
            .Where(r => !before.Contains(r.Id) && string.Equals(r.Trigger, "Manual", StringComparison.Ordinal))
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefault();
    }

    public static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var stream = File.Create(full);
        await JsonSerializer.SerializeAsync(stream, value, IndentedJson, cancellationToken);
    }

    private static async Task LoginAsDbaAsync(PlatformClient client, LabSecrets secrets, CancellationToken cancellationToken)
    {
        var perf = secrets.PerfDba();
        bool mustChange;
        try
        {
            mustChange = await client.LoginAsync(DbaUser, perf, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Base recién sembrada: el DBA todavía tiene la contraseña inicial y debe cambiarla (RNF-SEG-13).
            var initial = secrets.InitialDba();
            if (!await client.LoginAsync(DbaUser, initial, cancellationToken))
            {
                throw new InvalidOperationException("El DBA entró con la contraseña inicial pero la Api no pidió cambiarla; revisa la base.");
            }

            await client.ChangePasswordAsync(initial, perf, cancellationToken);
            mustChange = await client.LoginAsync(DbaUser, perf, cancellationToken);
        }

        if (mustChange)
        {
            throw new InvalidOperationException("La sesión del DBA sigue restringida a cambiar la contraseña.");
        }
    }

    private static async Task<T> UntilAsync<T>(Func<Task<T>> read, Func<T, bool> done, TimeSpan timeout, string what, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var value = await read();
            if (done(value))
            {
                return value;
            }

            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Se agotó el tiempo esperando {what} ({timeout.TotalSeconds:0} s).");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }
}
