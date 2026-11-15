using System.Globalization;
using System.Text.RegularExpressions;

namespace QueryDoctor.Application.Validation;

/// <summary>
/// Token numérico métrico extraído de una afirmación o evidencia en texto plano.
/// </summary>
public readonly record struct ExtractedNumericToken(
    string RawText,
    double Value,
    int DecimalPlaces,
    bool IsPercentage);

/// <summary>
/// Extrae cifras métricas de texto libre excluyendo marcadores de enmascaramiento y tokens estructurales,
/// y las contrasta contra una bolsa permitida aplicando la tolerancia documentada de redondeo (RA-02, RF-IA-03 b).
/// </summary>
public sealed class NumericEvidenceMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex MaskerTokenRegex = new(
        @"@qd_(?:str|num|bin|date|guid)_\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex RuleCodeRegex = new(
        @"\bREG-\d{1,3}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex GuidRegex = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex FindingHashRegex = new(
        @"\bF-[0-9A-Fa-f]{4,}\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex SqlParameterizedTypeRegex = new(
        @"\b(?:n?varchar|n?char|varbinary|binary|decimal|numeric|datetime2|datetimeoffset|time|float)\s*\(\s*\d+(?:\s*,\s*\d+)?\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex NodeMentionRegex = new(
        @"\b(?:nodo|node|relop)\s*#?\s*(?<id>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex MetricNumberRegex = new(
        @"(?<sign>(?<=^|[\s(=:,])-)?(?<![A-Za-z0-9_])(?<num>\d{1,3}(?:[ \u00A0]\d{3})+(?:[.,]\d+)?(?!\d)(?![.,]\d)|\d{1,3}(?:,\d{3})+(?:\.\d+)?(?!\d)(?!,\d)|\d{1,3}(?:\.\d{3})+(?:,\d+)(?!\d)|\d{1,3}(?:\.\d{3}){2,}(?!\d)(?!\.\d)|\d+(?:[.,]\d+)?)(?<pct>\s*%)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex CommaThousandsOnlyRegex = new(
        @"^\d{1,3}(?:,\d{3})+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex MultiDotThousandsOnlyRegex = new(
        @"^\d{1,3}(?:\.\d{3}){2,}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        RegexTimeout);

    private readonly ExplanationValidationOptions _options;

    public NumericEvidenceMatcher(ExplanationValidationOptions? options = null)
    {
        _options = options ?? ExplanationValidationOptions.Default;
        _options.Validate();
    }

    /// <summary>
    /// Extrae todas las cifras métricas candidatas de <paramref name="text"/> tras limpiar marcadores, reglas,
    /// identificadores, tipos SQL con precisión y nodos permitidos explícitamente mencionados.
    /// </summary>
    public static IReadOnlyList<ExtractedNumericToken> ExtractMetricTokens(
        string? text,
        IReadOnlyCollection<int>? allowedNodeIds = null,
        IReadOnlyCollection<string>? knownFindingIds = null,
        string? engineVersion = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var scrubbed = ScrubNonMetricTokens(text, allowedNodeIds, knownFindingIds, engineVersion);
        var matches = MetricNumberRegex.Matches(scrubbed);
        if (matches.Count == 0)
        {
            return [];
        }

        var tokens = new List<ExtractedNumericToken>(matches.Count);
        foreach (Match match in matches)
        {
            if (!TryParseToken(match, out var token))
            {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    /// <summary>
    /// Reúne los valores numéricos autorizados a partir de los hallazgos permitidos y las métricas de consulta.
    /// </summary>
    public static IReadOnlyList<double> CollectAllowedNumericValues(
        IEnumerable<ValidatedFindingInput> findings,
        IReadOnlyDictionary<string, double>? queryMetrics,
        string? engineVersion = null)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var values = new List<double>();
        var findingList = findings as IReadOnlyList<ValidatedFindingInput> ?? findings.ToList();
        var findingIds = findingList
            .Select(static f => f.FindingId)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .ToArray();

        foreach (var finding in findingList)
        {
            if (finding.NumericEvidence is not null)
            {
                foreach (var numericValue in finding.NumericEvidence.Values)
                {
                    AddIfFinite(values, numericValue);
                }
            }

            if (finding.Evidence is not null)
            {
                foreach (var entry in finding.Evidence)
                {
                    if (string.IsNullOrWhiteSpace(entry.Value))
                    {
                        continue;
                    }

                    var extracted = ExtractMetricTokens(
                        entry.Value,
                        allowedNodeIds: null,
                        knownFindingIds: findingIds,
                        engineVersion: engineVersion);

                    foreach (var token in extracted)
                    {
                        AddIfFinite(values, token.Value);
                    }
                }
            }
        }

        if (queryMetrics is not null)
        {
            foreach (var metricValue in queryMetrics.Values)
            {
                AddIfFinite(values, metricValue);
            }
        }

        return values;
    }

    /// <summary>
    /// Devuelve los tokens de <paramref name="tokens"/> que no coinciden dentro de tolerancia con ningún valor permitido.
    /// </summary>
    public IReadOnlyList<ExtractedNumericToken> FindUnmatchedTokens(
        IEnumerable<ExtractedNumericToken> tokens,
        IReadOnlyList<double> allowedValues)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(allowedValues);

        var unmatched = new List<ExtractedNumericToken>();
        foreach (var token in tokens)
        {
            if (!IsSupportedByEvidence(token, allowedValues))
            {
                unmatched.Add(token);
            }
        }

        return unmatched;
    }

    /// <summary>
    /// Determina si un token extraído del texto concuerda con algún valor de evidencia bajo la tolerancia RA-02.
    /// </summary>
    public bool IsSupportedByEvidence(ExtractedNumericToken token, IReadOnlyList<double> allowedValues)
    {
        ArgumentNullException.ThrowIfNull(allowedValues);

        for (var i = 0; i < allowedValues.Count; i++)
        {
            var expected = allowedValues[i];
            if (MatchesWithinTolerance(token, expected))
            {
                return true;
            }

            // Equivalencia de escala porcentual: 0.85 ↔ 85% únicamente para tasas/proporciones en [0, 1].
            if (token.IsPercentage &&
                _options.AllowPercentageScaleEquivalence &&
                expected is >= 0.0 and <= 1.0 &&
                MatchesWithinTolerance(token, expected * 100.0))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesWithinTolerance(ExtractedNumericToken token, double expected)
    {
        if (double.IsNaN(expected) || double.IsInfinity(expected))
        {
            return false;
        }

        var halfStep = _options.AllowHalfLastDecimalStepRounding
            ? 0.5 * Math.Pow(10.0, -token.DecimalPlaces)
            : 0.0;

        var relative = _options.RelativeTolerance * Math.Abs(expected);
        var tolerance = Math.Max(_options.AbsoluteEpsilon, Math.Max(halfStep, relative));
        return Math.Abs(token.Value - expected) <= tolerance;
    }

    private static string ScrubNonMetricTokens(
        string text,
        IReadOnlyCollection<int>? allowedNodeIds,
        IReadOnlyCollection<string>? knownFindingIds,
        string? engineVersion)
    {
        var current = MaskerTokenRegex.Replace(text, " ");
        current = RuleCodeRegex.Replace(current, " ");
        current = GuidRegex.Replace(current, " ");
        current = FindingHashRegex.Replace(current, " ");
        current = SqlParameterizedTypeRegex.Replace(current, " ");

        if (!string.IsNullOrWhiteSpace(engineVersion))
        {
            current = Regex.Replace(
                current,
                @"(?<![A-Za-z0-9_])(?<!\d\.)" + Regex.Escape(engineVersion.Trim()) + @"(?![A-Za-z0-9_])(?!\.\d)",
                " ",
                RegexOptions.CultureInvariant,
                RegexTimeout);
        }

        if (knownFindingIds is { Count: > 0 })
        {
            foreach (var findingId in knownFindingIds
                         .Where(static id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.Ordinal)
                         .OrderByDescending(static id => id.Length))
            {
                current = Regex.Replace(
                    current,
                    @"(?<![A-Za-z0-9_-])" + Regex.Escape(findingId) + @"(?![A-Za-z0-9_-])",
                    " ",
                    RegexOptions.CultureInvariant,
                    RegexTimeout);
            }
        }

        if (allowedNodeIds is { Count: > 0 })
        {
            var nodeSet = allowedNodeIds as IReadOnlySet<int> ?? new HashSet<int>(allowedNodeIds);
            current = NodeMentionRegex.Replace(current, match =>
                int.TryParse(match.Groups["id"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeId) &&
                nodeSet.Contains(nodeId)
                    ? " "
                    : match.Value);
        }

        return current;
    }

    private static bool TryParseToken(Match match, out ExtractedNumericToken token)
    {
        token = default;
        var rawNum = match.Groups["num"].Value;
        var hasSign = match.Groups["sign"].Success;
        var isPercentage = match.Groups["pct"].Success;

        if (string.IsNullOrWhiteSpace(rawNum))
        {
            return false;
        }

        var normalized = NormalizeNumberString(rawNum, out var decimalPlaces);
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            double.IsNaN(value) ||
            double.IsInfinity(value))
        {
            return false;
        }

        if (hasSign)
        {
            value = -value;
        }

        token = new ExtractedNumericToken(match.Value, value, decimalPlaces, isPercentage);
        return true;
    }

    private static string NormalizeNumberString(string rawNum, out int decimalPlaces)
    {
        var compact = rawNum.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u00A0", string.Empty, StringComparison.Ordinal);

        var lastComma = compact.LastIndexOf(',');
        var lastDot = compact.LastIndexOf('.');

        string canonical;
        if (lastComma >= 0 && lastDot >= 0)
        {
            if (lastDot > lastComma)
            {
                canonical = compact.Replace(",", string.Empty, StringComparison.Ordinal);
            }
            else
            {
                canonical = compact.Replace(".", string.Empty, StringComparison.Ordinal)
                    .Replace(',', '.');
            }
        }
        else if (lastComma >= 0)
        {
            canonical = CommaThousandsOnlyRegex.IsMatch(compact)
                ? compact.Replace(",", string.Empty, StringComparison.Ordinal)
                : compact.Replace(',', '.');
        }
        else if (lastDot >= 0)
        {
            canonical = MultiDotThousandsOnlyRegex.IsMatch(compact)
                ? compact.Replace(".", string.Empty, StringComparison.Ordinal)
                : compact;
        }
        else
        {
            canonical = compact;
        }

        var dotIndex = canonical.IndexOf('.');
        decimalPlaces = dotIndex >= 0 ? canonical.Length - dotIndex - 1 : 0;
        return canonical;
    }

    private static void AddIfFinite(List<double> target, double value)
    {
        if (!double.IsNaN(value) && !double.IsInfinity(value))
        {
            target.Add(value);
        }
    }
}
