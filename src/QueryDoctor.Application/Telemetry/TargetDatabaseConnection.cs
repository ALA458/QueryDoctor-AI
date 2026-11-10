namespace QueryDoctor.Application.Telemetry;

/// <summary>
/// Configuración de conexión hacia una base de datos SQL Server o Azure SQL objetivo (RNF-SEG-01, RNF-SEG-03).
/// </summary>
public sealed class TargetDatabaseConnection
{
    private const int DefaultLockTimeoutMs = 5000;
    private const int DefaultConnectionTimeoutSeconds = 15;

    public TargetDatabaseConnection(
        string server,
        string database,
        string? userId = null,
        string? password = null,
        TargetEncryptionMode encryptionMode = TargetEncryptionMode.Mandatory,
        bool trustServerCertificate = false,
        bool allowLocalTestCertificates = false,
        int connectionTimeoutSeconds = DefaultConnectionTimeoutSeconds,
        int lockTimeoutMs = DefaultLockTimeoutMs)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            throw new ArgumentException("El nombre o dirección del servidor no puede estar vacío.", nameof(server));
        }

        if (string.IsNullOrWhiteSpace(database))
        {
            throw new ArgumentException("El nombre de la base de datos no puede estar vacío.", nameof(database));
        }

        Server = server.Trim();
        Database = database.Trim();
        UserId = userId?.Trim();
        Password = password;
        EncryptionMode = encryptionMode;
        TrustServerCertificate = trustServerCertificate;
        AllowLocalTestCertificates = allowLocalTestCertificates;
        ConnectionTimeoutSeconds = connectionTimeoutSeconds > 0 ? connectionTimeoutSeconds : DefaultConnectionTimeoutSeconds;
        LockTimeoutMs = lockTimeoutMs > 0 ? lockTimeoutMs : DefaultLockTimeoutMs;
    }

    /// <summary>
    /// Dirección o host del servidor SQL Server (ej. "localhost,1433" o "sql.midominio.com").
    /// </summary>
    public string Server { get; }

    /// <summary>
    /// Nombre de la base de datos objetivo con Query Store activo.
    /// </summary>
    public string Database { get; }

    /// <summary>
    /// Usuario con privilegios mínimos (VIEW DATABASE STATE / VIEW DATABASE PERFORMANCE STATE).
    /// </summary>
    public string? UserId { get; }

    /// <summary>
    /// Contraseña de la cuenta de conexión. Nunca debe exponerse en registros o auditorías.
    /// </summary>
    public string? Password { get; }

    /// <summary>
    /// Nivel de cifrado TLS obligatorio (RNF-SEG-03). Por defecto Mandatory.
    /// </summary>
    public TargetEncryptionMode EncryptionMode { get; }

    /// <summary>
    /// Indica si se confía ciegamente en el certificado del servidor.
    /// En producción DEBE ser false (RNF-SEG-03).
    /// </summary>
    public bool TrustServerCertificate { get; }

    /// <summary>
    /// Bandera explícita para habilitar certificados autofirmados exclusivamente en entornos locales de pruebas/desarrollo.
    /// Si es false y <see cref="TrustServerCertificate"/> es true, la conexión será rechazada con SecurityException.
    /// </summary>
    public bool AllowLocalTestCertificates { get; }

    /// <summary>
    /// Tiempo máximo en segundos para establecer la conexión TCP/TDS.
    /// </summary>
    public int ConnectionTimeoutSeconds { get; }

    /// <summary>
    /// Tiempo límite en milisegundos para adquisición de bloqueos (RNF-SEG-01, P1). Por defecto 5000 ms.
    /// </summary>
    public int LockTimeoutMs { get; }

    /// <summary>
    /// Identificador descriptivo seguro del destino sin credenciales (ej. "myserver/mydatabase").
    /// </summary>
    public string TargetDescriptor => $"{Server}/{Database}";
}
