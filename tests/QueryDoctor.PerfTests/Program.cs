using System.Text;

namespace QueryDoctor.PerfTests;

/// <summary>
/// S3-07 (RNF-RND-01, RNF-RND-02). Lo invoca lab/Run-Perf.ps1; no corre en la CI.
///   setup  --secrets DIR --out FILE [--base-url URL] [--timeout-seconds N]
///   ingest --secrets DIR --connection-id GUID --out FILE [--base-url URL] [--timeout-seconds N]
///   dashboard --secrets DIR --connection-id GUID --out CARPETA [--requests 500] [--warmup 20] [--concurrency 1,5]
///             [--repetitions 3] [--max-seconds 300] [--seed 3007] [--detail-sample 100]
/// Salida: JSON en --out (dashboard: JSON + CSV crudo + CSV de resumen en la carpeta). Códigos: 0 bien, 1 falló la
/// medición, 2 argumentos inválidos. Que una meta no se cumpla NO es un fallo: se reporta.
/// </summary>
internal static class Program
{
    private const string DefaultBaseUrl = "http://127.0.0.1:8080";

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        PerfArgs parsed;
        try
        {
            parsed = PerfArgs.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var baseUrl = new Uri(parsed.Optional("base-url", DefaultBaseUrl), UriKind.Absolute);
            var secrets = new LabSecrets(parsed.Required("secrets"));
            var output = parsed.Required("out");

            switch (parsed.Command)
            {
                case "setup":
                    {
                        var timeout = TimeSpan.FromSeconds(parsed.OptionalInt("timeout-seconds", 180, 10, 3600));
                        using var client = new PlatformClient(baseUrl, TimeSpan.FromSeconds(60));
                        var result = await IngestionCommands.SetupAsync(client, secrets, timeout, cts.Token);
                        await IngestionCommands.WriteJsonAsync(output, result, cts.Token);
                        Console.WriteLine($"setup: QD_Lab {result.Status} como conexión {result.ConnectionId}");
                        return 0;
                    }

                case "ingest":
                    {
                        var timeout = TimeSpan.FromSeconds(parsed.OptionalInt("timeout-seconds", 1800, 10, 7200));
                        var connectionId = parsed.RequiredGuid("connection-id");
                        using var client = new PlatformClient(baseUrl, TimeSpan.FromSeconds(60));
                        var result = await IngestionCommands.IngestAsync(client, secrets, connectionId, timeout, cts.Token);
                        await IngestionCommands.WriteJsonAsync(output, result, cts.Token);
                        Console.WriteLine(
                            $"ingest: {result.Run.Status} ({result.Run.Cause ?? "sin causa"}), {result.Run.RowsUpserted ?? 0} filas, " +
                            $"{result.ObservedSeconds:0.0} s observados desde el POST");
                        return 0;
                    }

                case "dashboard":
                    {
                        var options = new DashboardOptions(
                            parsed.RequiredGuid("connection-id"),
                            parsed.OptionalInt("requests", 500, 1, 100_000),
                            parsed.OptionalInt("warmup", 20, 0, 10_000),
                            parsed.OptionalIntList("concurrency", [1, 5], 1, 64),
                            parsed.OptionalInt("repetitions", 3, 1, 20),
                            parsed.OptionalInt("max-seconds", 300, 10, 7200),
                            parsed.OptionalInt("seed", 3007, 0, int.MaxValue),
                            parsed.OptionalInt("detail-sample", 100, 1, 5000));

                        // 120 s por solicitud: por encima de los 60 s con los que Nginx corta (504), para que el corte se
                        // vea como respuesta de Nginx y no como tiempo límite del cliente.
                        using var client = new PlatformClient(baseUrl, TimeSpan.FromSeconds(120));
                        var (report, samples) = await DashboardCommands.RunAsync(client, secrets, options, cts.Token);
                        Directory.CreateDirectory(output);
                        await IngestionCommands.WriteJsonAsync(Path.Combine(output, "dashboard-report.json"), report, cts.Token);
                        await DashboardCommands.WriteSamplesCsvAsync(Path.Combine(output, "dashboard-samples.csv"), samples, cts.Token);
                        await DashboardCommands.WriteSummaryCsvAsync(Path.Combine(output, "dashboard-summary.csv"), report, cts.Token);
                        Console.WriteLine($"dashboard: {report.QueriesInList} consultas en el listado, {samples.Count} solicitudes medidas");
                        return 0;
                    }

                default:
                    Console.Error.WriteLine($"Comando desconocido: {parsed.Command}.");
                    PrintUsage();
                    return 2;
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or HttpRequestException or IOException
            or TaskCanceledException or UriFormatException or System.Text.Json.JsonException)
        {
            // Solo el mensaje: ninguno de estos lleva contraseñas (LabSecrets y PlatformClient nunca las incluyen).
            Console.Error.WriteLine($"Error de la medición: {ex.Message}");
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Uso: dotnet run --project tests/QueryDoctor.PerfTests -- <comando> [opciones]");
        Console.WriteLine("  setup  --secrets <carpeta> --out <archivo.json> [--base-url http://127.0.0.1:8080] [--timeout-seconds 180]");
        Console.WriteLine("  ingest --secrets <carpeta> --connection-id <guid> --out <archivo.json> [--base-url ...] [--timeout-seconds 1800]");
        Console.WriteLine("  dashboard --secrets <carpeta> --connection-id <guid> --out <carpeta> [--requests 500] [--warmup 20]");
        Console.WriteLine("            [--concurrency 1,5] [--repetitions 3] [--max-seconds 300] [--seed 3007] [--detail-sample 100]");
    }
}
