namespace QueryDoctor.Infrastructure.Security;

/// <summary>
/// Opciones de configuración para <see cref="SecretProtector"/>.
/// Permite inyectar la clave maestra desde configuración, variable de entorno o archivo de secreto (RNF-SEG-04).
/// </summary>
public sealed class SecretProtectorOptions
{
    /// <summary>
    /// Nombre de la sección de configuración en appsettings o variables de entorno.
    /// </summary>
    public const string SectionName = "Security:SecretProtector";

    /// <summary>
    /// Clave maestra externa en formato Base64 (32 bytes decodificados) o hexadecimal (64 caracteres).
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// Ruta alternativa a un archivo que contiene la clave maestra (ej. Docker secret en /run/secrets/master_encryption_key).
    /// </summary>
    public string? MasterKeyPath { get; set; }
}
