namespace QueryDoctor.Application.Telemetry;

/// <summary>Clase cerrada de fallo al hablar con la base objetivo, independiente del cliente SQL.</summary>
public enum TelemetryFailureKind
{
    InvalidCredentials,
    ServerUnreachable,
    TlsCertificateInvalid,
    PermissionDenied,
    Timeout,
    Unknown,
}

/// <summary>
/// Fallo de <see cref="ITelemetrySource"/> ya clasificado (S1-09, RF-CFG-02). El mensaje es fijo por clase y
/// <b>no</b> lleva la excepción original como InnerException: el texto del motor puede incluir el servidor, el
/// usuario o fragmentos de la cadena de conexión, y quien registre esta excepción no debe verlos (RNF-SEG-09).
/// </summary>
public sealed class TelemetrySourceException : Exception
{
    public TelemetrySourceException()
        : this(TelemetryFailureKind.Unknown, null)
    {
    }

    public TelemetrySourceException(string message)
        : base(message)
    {
    }

    public TelemetrySourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public TelemetrySourceException(TelemetryFailureKind kind, int? sqlErrorNumber)
        : base($"Fallo de telemetría: {kind}" + (sqlErrorNumber is null ? string.Empty : $" (error {sqlErrorNumber})"))
    {
        Kind = kind;
        SqlErrorNumber = sqlErrorNumber;
    }

    public TelemetryFailureKind Kind { get; }

    /// <summary>Número de error del motor (p. ej. 18456); es un código, no contiene datos.</summary>
    public int? SqlErrorNumber { get; }
}
