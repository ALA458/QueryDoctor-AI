namespace QueryDoctor.LabTests;

/// <summary>
/// Dónde está el laboratorio. QD_LAB_SQL activa las pruebas (p. ej. "localhost"); la contraseña de qd_reader se lee
/// del archivo de secretos local, nunca de una variable con el valor ni del repo (RNF-SEG-04).
/// </summary>
internal static class LabEnvironment
{
    public const string ServerVariable = "QD_LAB_SQL";
    public const string ReaderPasswordFileVariable = "QD_LAB_READER_PASSWORD_FILE";

    public const string SkipReason =
        "Sin laboratorio: define QD_LAB_SQL (p. ej. localhost) con el contenedor sqlserver arriba y " +
        "lab/sql/11-probe-fixtures.sql aplicado. Un Skipped no es evidencia (D7).";

    public const string Database = "QD_Lab";
    public const string DatabaseWithoutQueryStore = "QD_Lab_NoQS";
    public const string Reader = "qd_reader";
    public const string NoPermission = "qd_noperm";
    public const string PerformanceStateOnly = "qd_perf";

    public static string? Server =>
        Environment.GetEnvironmentVariable(ServerVariable) is { Length: > 0 } server ? server.Trim() : null;

    public static bool IsEnabled => Server is not null;

    public static string RequiredServer =>
        Server ?? throw new InvalidOperationException($"Falta {ServerVariable}.");

    /// <summary>Contraseña común de qd_reader, qd_noperm y qd_perf (04 y 11 la toman de READER_PASSWORD).</summary>
    public static string ReaderPassword()
    {
        var path = Environment.GetEnvironmentVariable(ReaderPasswordFileVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(FindRepositoryRoot(), "secrets", "mssql_reader_password.txt");

        // Con el laboratorio activado, que falte el secreto es un error de preparación, no un Skipped.
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"QD_LAB_SQL está definida pero no existe el archivo de contraseña del lector ({ReaderPasswordFileVariable}).");
        }

        return File.ReadAllText(path).Trim();
    }

    private static string FindRepositoryRoot()
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

/// <summary>[Fact] que se reporta como Skipped si el laboratorio no está configurado (xUnit 2, sin paquetes extra).</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LabFactAttribute : FactAttribute
{
    public LabFactAttribute()
    {
        if (!LabEnvironment.IsEnabled)
        {
            Skip = LabEnvironment.SkipReason;
        }
    }
}
