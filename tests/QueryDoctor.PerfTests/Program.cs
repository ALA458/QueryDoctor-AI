using System.Text;

namespace QueryDoctor.PerfTests;

/// <summary>
/// S3-07 (RNF-RND-01, RNF-RND-02). Lo invoca lab/Run-Perf.ps1; no corre en la CI.
///   setup  --secrets DIR --out FILE [--base-url URL] [--timeout-seconds N]
///   ingest --secrets DIR --connection-id GUID --out FILE [--base-url URL] [--timeout-seconds N]
/// Salida: JSON en --out. Códigos: 0 bien, 1 falló la medición, 2 argumentos inválidos.
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
    }
}
