using QueryDoctor.Domain.Core;

namespace QueryDoctor.LabTests;

/// <summary>
/// Plataforma del laboratorio bajo prueba (S3-06, RNF-COM-01): la matriz de CI corre la misma suite contra cada imagen
/// y QD_LAB_PLATFORM dice cuál es. <paramref name="ExpectedSupportLevel"/> es el nivel que la matriz de soporte
/// (Cuadro 4.16, <see cref="SupportMatrix"/>) le asigna; si la matriz cambia, cambia aquí y en docs/COMPATIBILIDAD.md.
/// </summary>
public sealed record LabPlatform(string Name, int MajorVersion, SupportLevel ExpectedSupportLevel)
{
    public const string Variable = "QD_LAB_PLATFORM";

    public static readonly LabPlatform SqlServer2022 = new("sql2022", 16, SupportLevel.Tested);
    public static readonly LabPlatform SqlServer2019 = new("sql2019", 15, SupportLevel.Tested);

    /// <summary>Prefijo de ProductVersion ("16.", "15.").</summary>
    public string VersionPrefix => $"{MajorVersion}.";

    /// <summary>sql2022 por omisión (el contenedor del compose); sql2019 en la matriz de CI.</summary>
    public static LabPlatform Current =>
        Environment.GetEnvironmentVariable(Variable)?.Trim().ToLowerInvariant() switch
        {
            null or "" or "sql2022" => SqlServer2022,
            "sql2019" => SqlServer2019,
            var other => throw new InvalidOperationException($"{Variable}='{other}' no es una plataforma del laboratorio (sql2022, sql2019)."),
        };
}
