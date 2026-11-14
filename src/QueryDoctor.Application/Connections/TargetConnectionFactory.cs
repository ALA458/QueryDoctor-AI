using QueryDoctor.Application.Telemetry;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Connections;

/// <summary>
/// Arma la conexión hacia la base objetivo a partir de la conexión registrada y sus credenciales descifradas.
/// Compartido por el sondeo (S1-09) y la ingesta (S1-11) para que ambos apliquen la MISMA política de TLS.
/// </summary>
public static class TargetConnectionFactory
{
    /// <param name="connection">Conexión registrada.</param>
    /// <param name="credentials">Credenciales ya descifradas (solo en memoria).</param>
    /// <param name="labTrustedCertificateServers">
    /// SOLO LABORATORIO (invariante 3): servidores cuyo certificado autofirmado se acepta. Vacío por defecto; el
    /// Worker no arranca con valores fuera de Development (LabCertificateGuard).
    /// </param>
    /// <param name="timeoutSeconds">Connect Timeout y tiempo límite de cada sentencia en el Gateway.</param>
    public static TargetDatabaseConnection Build(
        Connection connection,
        ConnectionCredentials credentials,
        IReadOnlyList<string> labTrustedCertificateServers,
        int timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(labTrustedCertificateServers);

        var labCertificate = labTrustedCertificateServers
            .Any(s => string.Equals(s, connection.Server, StringComparison.OrdinalIgnoreCase));

        return new TargetDatabaseConnection(
            connection.Server,
            connection.DatabaseName,
            credentials.UserName,
            credentials.Password,
            connection.Encryption == ConnectionEncryption.Strict ? TargetEncryptionMode.Strict : TargetEncryptionMode.Mandatory,
            trustServerCertificate: labCertificate,
            allowLocalTestCertificates: labCertificate,
            connectionTimeoutSeconds: timeoutSeconds);
    }
}
