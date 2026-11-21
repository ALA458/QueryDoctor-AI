namespace QueryDoctor.PerfTests;

/// <summary>
/// Contraseñas del laboratorio leídas de la carpeta <c>secrets/</c> (ignorada por Git; RNF-SEG-04). Nunca llegan por la
/// línea de comandos ni se imprimen.
/// </summary>
public sealed class LabSecrets
{
    public const string InitialDbaFile = "initial_dba_password.txt";
    public const string PerfDbaFile = "perf_dba_password.txt";
    public const string ReaderFile = "mssql_reader_password.txt";

    private readonly string _directory;

    public LabSecrets(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>Contraseña inicial del DBA <c>dba</c> que siembra la Api (InitialDba__PasswordFile).</summary>
    public string InitialDba() => Read(InitialDbaFile);

    /// <summary>Contraseña que la medición le pone al DBA en el primer ingreso (cambio obligatorio, RNF-SEG-13).</summary>
    public string PerfDba() => Read(PerfDbaFile);

    /// <summary>Contraseña de qd_reader (lab/sql/04-readonly-login.sql).</summary>
    public string Reader() => Read(ReaderFile);

    private string Read(string file)
    {
        var path = Path.Combine(_directory, file);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Falta el secreto {file} en la carpeta de secretos indicada.");
        }

        var value = File.ReadAllText(path).Trim();
        return value.Length > 0 ? value : throw new InvalidOperationException($"El secreto {file} está vacío.");
    }
}
