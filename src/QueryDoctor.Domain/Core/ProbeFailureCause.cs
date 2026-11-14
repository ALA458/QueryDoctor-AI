namespace QueryDoctor.Domain.Core;

/// <summary>
/// Causa cerrada por la que un sondeo de conexión no la activó (RF-CFG-02, RF-CFG-03, CA-HU01.2, CA-HU01.3).
/// Se guarda el código, nunca el texto de la excepción del motor: así no se filtran nombres de servidor,
/// usuarios ni fragmentos de la cadena de conexión (RNF-SEG-09).
/// </summary>
public enum ProbeFailureCause
{
    /// <summary>Usuario o contraseña rechazados por el servidor.</summary>
    InvalidCredentials,

    /// <summary>El servidor no responde o el nombre no se resuelve.</summary>
    ServerUnreachable,

    /// <summary>El certificado TLS del servidor no es de confianza (RNF-SEG-03).</summary>
    TlsCertificateInvalid,

    /// <summary>La cuenta no tiene VIEW DATABASE STATE ni VIEW DATABASE PERFORMANCE STATE.</summary>
    MissingPermission,

    /// <summary>El sondeo superó el tiempo límite configurado (RF-CFG-02).</summary>
    Timeout,

    /// <summary>Versión anterior a SQL Server 2016 (13.x) (RF-CFG-03).</summary>
    UnsupportedVersion,

    /// <summary>La plataforma declarada no coincide con la detectada.</summary>
    PlatformMismatch,

    /// <summary>No se pudo leer el estado de Query Store.</summary>
    QueryStoreUnavailable,

    /// <summary>Cualquier otro fallo.</summary>
    Unknown,
}

/// <summary>Mensaje fijo por código, el único texto de un fallo que ve el usuario.</summary>
public static class ProbeFailureMessages
{
    /// <summary>Versión mínima soportada que se indica al rechazar el registro (RF-CFG-03).</summary>
    public const string MinimumSupportedVersion = "SQL Server 2016 (13.x)";

    public static string For(ProbeFailureCause cause) => cause switch
    {
        ProbeFailureCause.InvalidCredentials => "El servidor rechazó el usuario o la contraseña.",
        ProbeFailureCause.ServerUnreachable => "El servidor no responde o su nombre no se pudo resolver.",
        ProbeFailureCause.TlsCertificateInvalid => "El certificado TLS del servidor no es de confianza.",
        ProbeFailureCause.MissingPermission =>
            "La cuenta no tiene VIEW DATABASE STATE ni VIEW DATABASE PERFORMANCE STATE sobre la base.",
        ProbeFailureCause.Timeout => "El sondeo superó el tiempo límite configurado.",
        ProbeFailureCause.UnsupportedVersion =>
            $"La versión del motor no está soportada. La versión mínima es {MinimumSupportedVersion}.",
        ProbeFailureCause.PlatformMismatch => "La plataforma declarada no coincide con la detectada en el servidor.",
        ProbeFailureCause.QueryStoreUnavailable => "No se pudo leer el estado de Query Store de la base.",
        _ => "No se pudo sondear la conexión.",
    };
}
