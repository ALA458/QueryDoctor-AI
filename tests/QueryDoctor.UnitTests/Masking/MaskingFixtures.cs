using System.Xml.Linq;
using static QueryDoctor.UnitTests.Masking.MaskingSentinels;

namespace QueryDoctor.UnitTests.Masking;

/// <summary>
/// Planes reales del laboratorio (S1-07/S1-12, <c>tests/fixtures/plans</c>) con centinelas sembrados en cada
/// portador de literales del Showplan: StatementText, ParameterCompiledValue, ParameterRuntimeValue,
/// ConstValue, ScalarString, Expression y un comentario XML.
/// </summary>
internal static class MaskingFixtures
{
    private static readonly XNamespace Showplan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    public static string SeededStatementText =>
        $"SELECT o.OrderId /* {CommentSecret} */ FROM dbo.Orders AS o -- {NestedCommentSecret}\n" +
        $"WHERE o.CustomerCode = N'{UnicodeSecret}' AND o.Note = '{EscapedSecret}' AND o.Id = {BigNumber} " +
        $"AND o.Amount > {DecimalNumber} AND o.Hash = {Hex} AND o.CreatedAt >= '{Date}' AND o.Session = '{GuidValue}'";

    public static IReadOnlyList<string> FixturePaths() =>
        Directory.GetFiles(FixturesDirectory(), "*.sqlplan")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();

    public static string Read(string fileName) => File.ReadAllText(Path.Combine(FixturesDirectory(), fileName));

    /// <summary>Devuelve el plan con centinelas en todos los portadores de literales que tenga.</summary>
    public static string Seed(string planXml)
    {
        var document = XDocument.Parse(planXml);
        var root = document.Root!;

        root.AddFirst(new XComment($" {CommentSecret} {Secret} "));

        foreach (var attribute in root.Descendants().Attributes("StatementText"))
        {
            attribute.Value = SeededStatementText;
        }

        foreach (var reference in root.Descendants(Showplan + "ColumnReference"))
        {
            if (reference.Attribute("ParameterCompiledValue") is { } compiled)
            {
                compiled.Value = $"N'{CompiledValueSecret}'";
            }

            if (reference.Attribute("ParameterRuntimeValue") is { } runtime)
            {
                runtime.Value = $"N'{RuntimeValueSecret}'";
            }
        }

        foreach (var parameterList in root.Descendants(Showplan + "ParameterList"))
        {
            parameterList.Add(new XElement(
                Showplan + "ColumnReference",
                new XAttribute("Column", "@qd_seed"),
                new XAttribute("ParameterDataType", "bigint"),
                new XAttribute("ParameterCompiledValue", $"({CompiledNumber})"),
                new XAttribute("ParameterRuntimeValue", $"({RuntimeNumber})")));
        }

        foreach (var attribute in root.Descendants().Attributes("ConstValue"))
        {
            attribute.Value = $"N'{ConstSecret}'";
        }

        foreach (var attribute in root.Descendants().Attributes("ScalarString"))
        {
            attribute.Value += $" AND [seed].[c]=N'{PredicateSecret}' OR [seed].[n]=({PredicateNumber})";
        }

        foreach (var attribute in root.Descendants().Attributes("Expression"))
        {
            attribute.Value += $"=N'{Secret}'";
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string FixturesDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "fixtures", "plans");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró tests/fixtures/plans.");
    }
}
