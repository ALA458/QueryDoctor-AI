using System.Globalization;

namespace QueryDoctor.Domain.Core;

/// <summary>Plataforma que el motor reporta con SERVERPROPERTY('EngineEdition').</summary>
public enum DetectedPlatform
{
    SqlServer,
    AzureSqlDatabase,
    AzureSqlManagedInstance,

    /// <summary>Synapse, Edge u otra edición que el MVP no contempla.</summary>
    Other,
}

/// <summary>Función que el MVP omite, con aviso, porque la versión detectada no la ofrece (RNF-COM-04).</summary>
public enum OmittedFeature
{
    /// <summary>Uso de TempDB y bytes de log por intervalo (RF-ING-03): columnas de 2017+ y Azure SQL.</summary>
    TempDbAndLogBytes,
}

/// <summary>Resultado de evaluar un motor contra la matriz de soporte.</summary>
/// <param name="Platform">Plataforma detectada.</param>
/// <param name="MajorVersion">Versión mayor de ProductVersion; null si no se pudo leer.</param>
/// <param name="SupportLevel">Nivel según el Cuadro 4.16 del SRS (RF-CFG-07).</param>
/// <param name="FailureCause">Si no es null, la conexión no se activa (CA-HU01.3, D4).</param>
public sealed record SupportAssessment(
    DetectedPlatform Platform,
    int? MajorVersion,
    SupportLevel SupportLevel,
    ProbeFailureCause? FailureCause);

/// <summary>
/// Matriz de soporte por plataforma y versión: Cuadro 4.16 del SRS v1.1 (idéntico a la Adenda §9).
/// Función pura: mismo motor, mismo resultado; sin E/S.
/// </summary>
/// <remarks>
/// Deuda (D4): Azure SQL Database y SQL Server 2025 (17.x) quedan como Probado porque así lo dice la matriz
/// ("Probado" y "2022 o superior"), pero aún no se han corrido pruebas de aceptación en esas plataformas.
/// </remarks>
public static class SupportMatrix
{
    /// <summary>SQL Server 2016 (13.x), la versión mínima de RF-CFG-03.</summary>
    public const int MinimumMajorVersion = 13;

    /// <summary>SQL Server 2022 (16.x): desde aquí basta VIEW DATABASE PERFORMANCE STATE.</summary>
    public const int PerformanceStateMajorVersion = 16;

    public const string ViewDatabaseState = "VIEW DATABASE STATE";
    public const string ViewDatabasePerformanceState = "VIEW DATABASE PERFORMANCE STATE";

    /// <summary>Columnas de sys.query_store_runtime_stats que exige RF-ING-03.</summary>
    public static readonly IReadOnlyList<string> TempDbAndLogColumns = ["avg_tempdb_space_used", "avg_log_bytes_used"];

    /// <summary>SERVERPROPERTY('EngineEdition'): 1–4 SQL Server, 5 Azure SQL Database, 8 Managed Instance.</summary>
    public static DetectedPlatform PlatformFromEngineEdition(int engineEdition) => engineEdition switch
    {
        1 or 2 or 3 or 4 => DetectedPlatform.SqlServer,
        5 => DetectedPlatform.AzureSqlDatabase,
        8 => DetectedPlatform.AzureSqlManagedInstance,
        _ => DetectedPlatform.Other,
    };

    /// <summary>Evalúa versión, edición y plataforma declarada contra la matriz.</summary>
    public static SupportAssessment Assess(ConnectionPlatform declared, string? productVersion, int engineEdition)
    {
        var platform = PlatformFromEngineEdition(engineEdition);
        var major = ParseMajorVersion(productVersion);

        // Managed Instance se declara como SQL Server: el enum de plataformas del SRS no la distingue (RF-CFG-01).
        var matchesDeclared = (declared, platform) switch
        {
            (ConnectionPlatform.SqlServer, DetectedPlatform.SqlServer) => true,
            (ConnectionPlatform.SqlServer, DetectedPlatform.AzureSqlManagedInstance) => true,
            (ConnectionPlatform.AzureSqlDatabase, DetectedPlatform.AzureSqlDatabase) => true,
            _ => false,
        };

        if (!matchesDeclared)
        {
            return new SupportAssessment(platform, major, SupportLevel.NotSupported, ProbeFailureCause.PlatformMismatch);
        }

        // Azure SQL Database reporta 12.x en ProductVersion: no se le aplica el corte de versión de SQL Server.
        if (platform == DetectedPlatform.AzureSqlDatabase)
        {
            return new SupportAssessment(platform, major, SupportLevel.Tested, null);
        }

        if (platform == DetectedPlatform.AzureSqlManagedInstance)
        {
            return new SupportAssessment(platform, major, SupportLevel.CompatibleUntested, null);
        }

        if (major is null)
        {
            return new SupportAssessment(platform, null, SupportLevel.NotSupported, ProbeFailureCause.Unknown);
        }

        var level = major switch
        {
            < MinimumMajorVersion => SupportLevel.NotSupported,
            13 or 14 => SupportLevel.CompatibleUntested, // 2016 (DA-12) y 2017 (D4)
            _ => SupportLevel.Tested,                    // 2019, 2022 o superior
        };

        return new SupportAssessment(
            platform,
            major,
            level,
            level == SupportLevel.NotSupported ? ProbeFailureCause.UnsupportedVersion : null);
    }

    /// <summary>
    /// Permiso mínimo de la matriz: VIEW DATABASE STATE; en 2022+ y Azure también vale
    /// VIEW DATABASE PERFORMANCE STATE (VIEW DATABASE STATE lo incluye).
    /// </summary>
    public static bool HasMinimumPermission(SupportAssessment assessment, IEnumerable<string> databasePermissions)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(databasePermissions);

        var permissions = databasePermissions
            .Select(p => p.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (permissions.Contains(ViewDatabaseState))
        {
            return true;
        }

        var acceptsPerformanceState = assessment.Platform != DetectedPlatform.SqlServer
            || assessment.MajorVersion >= PerformanceStateMajorVersion;

        return acceptsPerformanceState && permissions.Contains(ViewDatabasePerformanceState);
    }

    /// <summary>
    /// Funciones omitidas según las columnas que de verdad expone sys.query_store_runtime_stats,
    /// no según el número de versión (así también vale para Azure). Orden fijo para que sea determinista.
    /// </summary>
    public static IReadOnlyList<OmittedFeature> OmittedFeatures(IEnumerable<string> runtimeStatsColumns)
    {
        ArgumentNullException.ThrowIfNull(runtimeStatsColumns);

        var columns = runtimeStatsColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var omitted = new List<OmittedFeature>();

        if (!TempDbAndLogColumns.All(columns.Contains))
        {
            omitted.Add(OmittedFeature.TempDbAndLogBytes);
        }

        return omitted;
    }

    /// <summary>Mensaje fijo del aviso de cada función omitida (RF-CFG-07, RNF-COM-04).</summary>
    public static string MessageFor(OmittedFeature feature) => feature switch
    {
        OmittedFeature.TempDbAndLogBytes =>
            "Esta versión no registra uso de TempDB ni bytes de log en Query Store; esas métricas quedarán vacías.",
        _ => "Función no disponible en esta versión.",
    };

    private static int? ParseMajorVersion(string? productVersion)
    {
        if (string.IsNullOrWhiteSpace(productVersion))
        {
            return null;
        }

        var dot = productVersion.IndexOf('.', StringComparison.Ordinal);
        var head = dot >= 0 ? productVersion[..dot] : productVersion;

        return int.TryParse(head.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : null;
    }
}

/// <summary>Regla de ingesta a partir del estado de Query Store (D5; la usa S1-11 para CA-HU02.2).</summary>
public static class QueryStoreState
{
    /// <summary>
    /// Se puede ingerir si Query Store está en READ_WRITE o READ_ONLY (en solo lectura los datos se pueden leer).
    /// OFF, ERROR, desconocido o sin dato: se omite la ingesta y se informa la causa.
    /// </summary>
    public static bool CanIngest(string? actualStateDesc) =>
        actualStateDesc is not null
        && (actualStateDesc.Equals("READ_WRITE", StringComparison.OrdinalIgnoreCase)
            || actualStateDesc.Equals("READ_ONLY", StringComparison.OrdinalIgnoreCase));
}
