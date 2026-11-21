using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Connections;

// S3-06 (RNF-COM-01): SupportMatrix debe coincidir exactamente con la matriz declarada en docs/COMPATIBILIDAD.md.
// Si alguien cambia un nivel en el código o en el documento sin cambiar el otro, esta prueba queda en rojo.
public sealed class SupportMatrixDocumentTests
{
    private const string Start = "<!-- matriz-soporte:inicio -->";
    private const string End = "<!-- matriz-soporte:fin -->";

    private static readonly Dictionary<string, SupportLevel> Levels = new(StringComparer.Ordinal)
    {
        ["Probado"] = SupportLevel.Tested,
        ["Compatible sin pruebas"] = SupportLevel.CompatibleUntested,
        ["No soportado"] = SupportLevel.NotSupported,
    };

    public static TheoryData<string, string, string, int, string> Rows()
    {
        var data = new TheoryData<string, string, string, int, string>();
        foreach (var row in ReadRows())
        {
            data.Add(row.Platform, row.Declared, row.Version, row.Edition, row.Level);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void CadaFilaDelDocumentoCoincideConSupportMatrix(string platform, string declared, string version, int edition, string level)
    {
        var assessment = SupportMatrix.Assess(Enum.Parse<ConnectionPlatform>(declared), version, edition);

        Assert.True(Levels.TryGetValue(level, out var expected), $"Nivel desconocido en el documento: '{level}'.");
        Assert.True(expected == assessment.SupportLevel, $"{platform}: el documento dice {level} y SupportMatrix da {assessment.SupportLevel}.");
    }

    [Fact]
    public void LaMatrizDelDocumentoCubreLasOchoFilasDelCuadro416()
    {
        var rows = ReadRows();

        Assert.Equal(8, rows.Count);
        Assert.Equal(rows.Count, rows.Select(r => r.Platform).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(rows, r => r.Level == "Probado");
        Assert.Contains(rows, r => r.Level == "Compatible sin pruebas");
        Assert.Contains(rows, r => r.Level == "No soportado");
    }

    private static List<(string Platform, string Declared, string Version, int Edition, string Level)> ReadRows()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "COMPATIBILIDAD.md"));
        var start = text.IndexOf(Start, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "docs/COMPATIBILIDAD.md no tiene las marcas de la matriz de soporte.");

        return text[(start + Start.Length)..end]
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('|') && !line.StartsWith("|---", StringComparison.Ordinal))
            .Skip(1) // encabezado
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Select(cells => (cells[0], cells[1], cells[2], int.Parse(cells[3], System.Globalization.CultureInfo.InvariantCulture), cells[4]))
            .ToList();
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QueryDoctor.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("No se encontró la raíz del repositorio (QueryDoctor.slnx).");
    }
}
