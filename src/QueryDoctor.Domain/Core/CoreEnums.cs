namespace QueryDoctor.Domain.Core;

/// <summary>Plataforma de la base de datos objetivo (RF-CFG-01).</summary>
public enum ConnectionPlatform
{
    SqlServer,
    AzureSqlDatabase,
}

/// <summary>Método de autenticación hacia la base objetivo (RF-CFG-01).</summary>
public enum AuthenticationMethod
{
    SqlAuthentication,
}

/// <summary>Modo de cifrado TLS hacia la base objetivo; nunca "desactivado" (RNF-SEG-03).</summary>
public enum ConnectionEncryption
{
    Mandatory,
    Strict,
}

/// <summary>Estado de una conexión registrada.</summary>
public enum ConnectionStatus
{
    Active,
    Disabled,
}

/// <summary>Nivel de soporte según la matriz de soporte del SRS (RF-CFG-07).</summary>
public enum SupportLevel
{
    Tested,
    CompatibleUntested,
    NotSupported,
}

/// <summary>Estado de análisis de un plan (RF-ING-09).</summary>
public enum PlanAnalysisStatus
{
    Pending,
    Analyzable,
    NotAnalyzable,
}

/// <summary>Tipo de ejecución reportado por Query Store.</summary>
public enum ExecutionType
{
    Regular,
    Aborted,
    Exception,
}

/// <summary>Cómo se inició una ingesta (RF-ING-06).</summary>
public enum IngestionTrigger
{
    Manual,
    Scheduled,
}

/// <summary>Resultado de una ingesta.</summary>
public enum IngestionStatus
{
    Running,
    Succeeded,
    Failed,
    Skipped,
}
