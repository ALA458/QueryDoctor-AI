namespace QueryDoctor.Application.Telemetry;

/// <summary>
/// Opciones de cifrado TLS para la conexión hacia la base de datos objetivo (RNF-SEG-03).
/// </summary>
public enum TargetEncryptionMode
{
    /// <summary>
    /// Exige cifrado TLS y valida el certificado del servidor (equivalente a Encrypt=Mandatory en SqlClient).
    /// </summary>
    Mandatory = 1,

    /// <summary>
    /// Exige cifrado estricto TLS 1.3 con validación de certificado (equivalente a Encrypt=Strict en SqlClient).
    /// </summary>
    Strict = 2
}
