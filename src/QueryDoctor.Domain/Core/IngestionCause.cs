namespace QueryDoctor.Domain.Core;

/// <summary>
/// Causa cerrada por la que una ingesta se omitió o falló (S1-11; RF-ING-08, RNF-FIA-01, CA-HU02.2). Se guarda el
/// nombre del código en core.ingestion_run.error_cause, nunca el texto de una excepción del motor: así no se filtran
/// servidor, usuario ni fragmentos de la cadena de conexión (RNF-SEG-09).
/// </summary>
public enum IngestionCause
{
    /// <summary>Query Store está en OFF: la ingesta se omite y se conservan los datos (RF-ING-08, CA-HU02.2).</summary>
    QueryStoreDisabled,

    /// <summary>Query Store está en ERROR, en un estado desconocido o no se pudo leer su estado (RF-ING-08).</summary>
    QueryStoreNotReadable,

    /// <summary>La conexión no está Active (pendiente, fallida, rechazada o deshabilitada).</summary>
    ConnectionNotActive,

    /// <summary>Ya hay una ingesta de esta conexión en curso (bloqueo por conexión).</summary>
    AlreadyRunning,

    /// <summary>La cuenta no puede leer Query Store.</summary>
    MissingPermission,

    /// <summary>El servidor rechazó el usuario o la contraseña.</summary>
    InvalidCredentials,

    /// <summary>El servidor no responde: fallo transitorio, se reintenta con espera exponencial (RNF-FIA-01).</summary>
    ServerUnreachable,

    /// <summary>El certificado TLS del servidor no es de confianza (RNF-SEG-03).</summary>
    TlsCertificateInvalid,

    /// <summary>Se superó un tiempo límite: fallo transitorio, se reintenta (RNF-FIA-01).</summary>
    Timeout,

    /// <summary>La ejecución quedó en Running porque el proceso se detuvo; la siguiente ingesta la cierra.</summary>
    Interrupted,

    /// <summary>Cualquier otro fallo (también los de persistencia).</summary>
    Unknown,
}

/// <summary>Mensaje fijo por código: el único texto de una ingesta omitida o fallida que ve el usuario.</summary>
public static class IngestionCauseMessages
{
    public static string For(IngestionCause cause) => cause switch
    {
        IngestionCause.QueryStoreDisabled =>
            "Query Store está deshabilitado en la base. La ingesta se omitió y los datos ya almacenados se conservan.",
        IngestionCause.QueryStoreNotReadable =>
            "El estado de Query Store impide la lectura. La ingesta se omitió y los datos ya almacenados se conservan.",
        IngestionCause.ConnectionNotActive => "La conexión no está activa.",
        IngestionCause.AlreadyRunning => "Ya hay una ingesta en curso para esta conexión.",
        IngestionCause.MissingPermission => "La cuenta no tiene permiso para leer Query Store.",
        IngestionCause.InvalidCredentials => "El servidor rechazó el usuario o la contraseña.",
        IngestionCause.ServerUnreachable => "El servidor no responde. La ingesta se reintentará.",
        IngestionCause.TlsCertificateInvalid => "El certificado TLS del servidor no es de confianza.",
        IngestionCause.Timeout => "La ingesta superó el tiempo límite. Se reintentará.",
        IngestionCause.Interrupted => "La ingesta se interrumpió antes de terminar; la siguiente retoma desde la marca de agua.",
        _ => "La ingesta falló por un error inesperado.",
    };
}

/// <summary>Causa cerrada por la que un plan quedó como no analizable (RF-ING-09, CA-HU03.3).</summary>
public enum PlanNotAnalyzableCause
{
    /// <summary>Query Store ya no devuelve el plan (se purgó entre la lectura de estadísticas y la del plan).</summary>
    PlanNotFound,

    /// <summary>query_plan es NULL.</summary>
    NullXml,

    /// <summary>query_plan es una cadena vacía o solo espacios.</summary>
    EmptyXml,

    /// <summary>XML truncado o inválido.</summary>
    MalformedXml,

    /// <summary>XML bien formado cuyo elemento raíz no es ShowPlanXML.</summary>
    NotShowplan,

    /// <summary>El plan supera el tamaño máximo que se procesa.</summary>
    TooLarge,
}
