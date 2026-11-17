using System.Text;

namespace QueryDoctor.EngineHarness;

internal static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (!TryParseArgs(args, out var options, out var errorMessage, out var showHelp))
        {
            Console.Error.WriteLine(errorMessage);
            PrintUsage();
            return 2;
        }

        if (showHelp)
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var manifestPath = ResolveManifestPath(options.ManifestPath);
            var thresholds = string.IsNullOrWhiteSpace(options.ThresholdsPath)
                ? null
                : EngineHarnessRunner.LoadThresholds(options.ThresholdsPath);

            var runner = new EngineHarnessRunner();
            var report = runner.EvaluateFromManifest(
                manifestPath,
                options.Corpus,
                options.Split,
                thresholds);

            var formatted = options.Format switch
            {
                "json" => ReportFormatter.ToJson(report),
                "markdown" => ReportFormatter.ToMarkdown(report),
                _ => ReportFormatter.ToTable(report),
            };

            Console.WriteLine(formatted);

            if (!string.IsNullOrWhiteSpace(options.OutputDir))
            {
                var outDir = Path.GetFullPath(options.OutputDir);
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "engine-metrics.json"), ReportFormatter.ToJson(report), Encoding.UTF8);
                File.WriteAllText(Path.Combine(outDir, "engine-metrics.md"), ReportFormatter.ToMarkdown(report), Encoding.UTF8);
            }

            if (options.CheckTargets && !report.AllMustTargetsMet)
            {
                Console.Error.WriteLine("Al menos una regla Debe quedó por debajo de las metas RNF-FUN-01 (P >= 90 %, R >= 80 %).");
                return 1;
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Error ejecutando el arnés del motor: {ex.Message}");
            return 2;
        }
    }

    private static string ResolveManifestPath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var relative = Path.Combine("tests", "fixtures", "engine-ground-truth.json");
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(relative);
    }

    private static bool TryParseArgs(
        string[] args,
        out CliOptions options,
        out string? error,
        out bool showHelp)
    {
        var manifest = (string?)null;
        var corpus = "all";
        var split = "all";
        var thresholds = (string?)null;
        var format = "table";
        var outputDir = (string?)null;
        var checkTargets = false;
        showHelp = false;
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                showHelp = true;
                options = new CliOptions(manifest, corpus, split, thresholds, format, outputDir, checkTargets);
                return true;
            }

            if (arg == "--check-targets")
            {
                checkTargets = true;
                continue;
            }

            if (!TryReadOptionValue(args, ref i, out var val, out error))
            {
                options = new CliOptions(manifest, corpus, split, thresholds, format, outputDir, checkTargets);
                return false;
            }

            switch (arg)
            {
                case "--manifest":
                    manifest = val;
                    break;
                case "--corpus":
                    corpus = val;
                    break;
                case "--split":
                    split = val;
                    break;
                case "--thresholds":
                    thresholds = val;
                    break;
                case "--format":
                    format = val.Trim().ToLowerInvariant();
                    if (format is not ("table" or "json" or "markdown"))
                    {
                        error = $"Formato inválido '{val}'. Use table, json o markdown.";
                        options = new CliOptions(manifest, corpus, split, thresholds, format, outputDir, checkTargets);
                        return false;
                    }
                    break;
                case "--output-dir":
                    outputDir = val;
                    break;
                default:
                    error = $"Opción no reconocida: '{arg}'.";
                    options = new CliOptions(manifest, corpus, split, thresholds, format, outputDir, checkTargets);
                    return false;
            }
        }

        options = new CliOptions(manifest, corpus, split, thresholds, format, outputDir, checkTargets);
        return true;
    }

    private static bool TryReadOptionValue(string[] args, ref int index, out string value, out string? error)
    {
        var flag = args[index];
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"Falta el valor para {flag}.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Uso: dotnet run --project tools/QueryDoctor.EngineHarness -- [opciones]");
        Console.WriteLine("  --manifest <ruta>      Ruta a engine-ground-truth.json (defecto: tests/fixtures/engine-ground-truth.json)");
        Console.WriteLine("  --corpus <valor>       all (defecto) | seeded | tpch");
        Console.WriteLine("  --split <valor>        all (defecto) | calibration | evaluation");
        Console.WriteLine("  --thresholds <ruta>    JSON opcional de RuleThresholds para calibración (S3-01)");
        Console.WriteLine("  --format <valor>       table (defecto) | markdown | json");
        Console.WriteLine("  --output-dir <carpeta> Guarda engine-metrics.json y engine-metrics.md");
        Console.WriteLine("  --check-targets        Retorna 1 si alguna regla incumple RNF-FUN-01 (P>=90%, R>=80%)");
    }

    private sealed record CliOptions(
        string? ManifestPath,
        string Corpus,
        string Split,
        string? ThresholdsPath,
        string Format,
        string? OutputDir,
        bool CheckTargets);
}
