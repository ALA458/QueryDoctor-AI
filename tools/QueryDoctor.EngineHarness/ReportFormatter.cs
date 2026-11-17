using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QueryDoctor.EngineHarness;

/// <summary>
/// Formatea los resultados del arnés en tabla de consola, Markdown o JSON reproducible (CA-HU20.1, RF-EVA-04).
/// </summary>
public static class ReportFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ToJson(EngineEvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string ToTable(EngineEvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Query Doctor AI — Arnes de Evaluacion del Motor (Catalogo v{report.CatalogVersion})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Umbrales hash : {report.ThresholdsHash}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Filtro        : corpus={report.CorpusFilter} | split={report.SplitFilter} | planes={report.TotalCases} (seeded={report.SeededCases}, tpch={report.TpchCases})");
        sb.AppendLine();
        AppendConsoleTable(sb, "RESUMEN GLOBAL (REG-01..REG-04)", report.Rules);

        if (report.SeededCases > 0 && report.TpchCases > 0)
        {
            sb.AppendLine();
            AppendConsoleTable(sb, "DESGLOSE: ESCENARIOS SEMBRADOS (seeded)", report.SeededRules);
            sb.AppendLine();
            AppendConsoleTable(sb, "DESGLOSE: CARGA TPC-H (tpch)", report.TpchRules);
        }

        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Tolerancia REG-11 : esperados={report.NotAnalyzable.ExpectedNotAnalyzable}, clasificados={report.NotAnalyzable.CorrectlyClassifiedReg11}, inesperados={report.NotAnalyzable.UnexpectedNotAnalyzable}");

        if (report.Diffs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"Discrepancias detectadas ({report.Diffs.Count}):");
            foreach (var d in report.Diffs)
            {
                var nodesText = d.Nodes.Count > 0 ? $" nodos=[{string.Join(",", d.Nodes)}]" : string.Empty;
                sb.AppendLine(CultureInfo.InvariantCulture, $"  - [{d.Kind}] {d.CaseId} ({d.Corpus}/{d.Split}) {d.RuleId}{nodesText}: {d.Detail}");
            }
        }

        return sb.ToString();
    }

    public static string ToMarkdown(EngineEvaluationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var sb = new StringBuilder();
        sb.AppendLine("# Informe de evaluación del motor determinístico (`RF-EVA-01`, `CA-HU20.1`)");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Catálogo de reglas:** `v{report.CatalogVersion}`");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Hash de umbrales (`RuleThresholds`):** `{report.ThresholdsHash}`");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Corpus / Split:** `{report.CorpusFilter}` / `{report.SplitFilter}` (`{report.TotalCases}` planes: `{report.SeededCases}` sembrados, `{report.TpchCases}` TPC-H)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- **Metas de referencia (`RNF-FUN-01`, P >= 90 %, R >= 80 %):** {(report.AllMustTargetsMet ? "CUMPLE en reglas con muestras" : "REVISAR reglas bajo meta")}");
        sb.AppendLine();
        sb.AppendLine("## Métricas globales por regla (`REG-01`–`REG-04`)");
        sb.AppendLine();
        AppendMarkdownTable(sb, report.Rules);

        if (report.SeededCases > 0 && report.TpchCases > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Desglose en escenarios sembrados (`seeded`)");
            sb.AppendLine();
            AppendMarkdownTable(sb, report.SeededRules);
            sb.AppendLine();
            sb.AppendLine("### Desglose en carga TPC-H (`tpch`)");
            sb.AppendLine();
            AppendMarkdownTable(sb, report.TpchRules);
        }

        sb.AppendLine();
        sb.AppendLine("## Tolerancia a planes no analizables (`REG-11` / `RF-MOT-16`)");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- No analizables esperados: `{report.NotAnalyzable.ExpectedNotAnalyzable}` | Clasificados con `REG-11`: `{report.NotAnalyzable.CorrectlyClassifiedReg11}` | Fallos inesperados en planes válidos: `{report.NotAnalyzable.UnexpectedNotAnalyzable}`");

        if (report.Diffs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Discrepancias (`FP` / `FN`)");
            sb.AppendLine();
            sb.AppendLine("| Tipo | Caso | Corpus / Split | Regla | Nodos | Detalle |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var d in report.Diffs)
            {
                var nodesText = d.Nodes.Count > 0 ? string.Join(", ", d.Nodes) : "—";
                sb.AppendLine(CultureInfo.InvariantCulture, $"| `{d.Kind}` | `{d.CaseId}` | `{d.Corpus}/{d.Split}` | `{d.RuleId}` | {nodesText} | {d.Detail} |");
            }
        }

        return sb.ToString();
    }

    private static void AppendConsoleTable(StringBuilder sb, string heading, IReadOnlyList<RuleMetricsSummary> rows)
    {
        sb.AppendLine(heading);
        sb.AppendLine("Regla  | Pos | TP | FP | FN | Precision | Exhaustividad |   F1   | RNF-FUN-01");
        sb.AppendLine("-------+-----+----+----+----+-----------+---------------+--------+-----------");
        foreach (var r in rows)
        {
            var gate = FormatGate(r);
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,-6} | {1,3} | {2,2} | {3,2} | {4,2} | {5,9} | {6,13} | {7,6} | {8}",
                r.RuleId,
                r.ExpectedPositives,
                r.TruePositives,
                r.FalsePositives,
                r.FalseNegatives,
                FormatPct(r.Precision),
                FormatPct(r.Recall),
                FormatPct(r.F1),
                gate));
        }
    }

    private static void AppendMarkdownTable(StringBuilder sb, IReadOnlyList<RuleMetricsSummary> rows)
    {
        sb.AppendLine("| Regla | Descripción | Positivos | TP | FP | FN | Precisión | Exhaustividad | F1 | Meta `RNF-FUN-01` |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var r in rows)
        {
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "| `{0}` | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |",
                r.RuleId,
                r.RuleName,
                r.ExpectedPositives,
                r.TruePositives,
                r.FalsePositives,
                r.FalseNegatives,
                FormatPct(r.Precision),
                FormatPct(r.Recall),
                FormatPct(r.F1),
                FormatGate(r)));
        }
    }

    private static string FormatPct(double? value) =>
        value.HasValue
            ? (value.Value * 100.0).ToString("0.0", CultureInfo.InvariantCulture) + " %"
            : "N/A";

    private static string FormatGate(RuleMetricsSummary row)
    {
        if (!row.MeetsPrecisionTarget.HasValue && !row.MeetsRecallTarget.HasValue)
        {
            return "Sin muestras";
        }

        var ok = (row.MeetsPrecisionTarget ?? true) && (row.MeetsRecallTarget ?? true);
        return ok ? "OK" : "BAJO META";
    }
}
