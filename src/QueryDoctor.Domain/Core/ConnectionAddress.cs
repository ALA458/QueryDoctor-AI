using System.Globalization;

namespace QueryDoctor.Domain.Core;

/// <summary>Motivo por el que un nombre de servidor o de base no se acepta.</summary>
public enum ConnectionAddressError
{
    None,
    Empty,
    TooLong,

    /// <summary>np:, lpc: o admin: cambian el protocolo o abren la conexión de administrador (DAC).</summary>
    ForbiddenProtocol,

    InvalidPort,
}

/// <summary>
/// Normaliza servidor y base antes de guardar y de buscar duplicados (RF-CFG-01, MODELO_DATOS §7).
/// No resuelve DNS: "srv", "srv.dominio" y su IP son conexiones distintas (limitación documentada).
/// </summary>
public static class ConnectionAddress
{
    public const int ServerMaxLength = 255;
    public const int DatabaseNameMaxLength = 128;

    private const string DefaultPort = "1433";
    private const string TcpPrefix = "tcp:";
    private static readonly string[] ForbiddenPrefixes = ["np:", "lpc:", "admin:"];

    /// <summary>
    /// Servidor normalizado: sin espacios, en minúsculas, sin "tcp:", sin ",1433" y sin punto final en el host.
    /// Conserva la instancia (srv\inst) y cualquier otro puerto. Rechaza np:, lpc: y admin:.
    /// </summary>
    public static bool TryNormalizeServer(string? input, out string normalized, out ConnectionAddressError error)
    {
        normalized = string.Empty;

        // Los nombres de host y de instancia no distinguen mayúsculas; los espacios nunca son válidos.
        var value = string.Concat((input ?? string.Empty).Where(c => !char.IsWhiteSpace(c)))
            .ToLowerInvariant();

        if (value.Length == 0)
        {
            error = ConnectionAddressError.Empty;
            return false;
        }

        if (ForbiddenPrefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal)))
        {
            error = ConnectionAddressError.ForbiddenProtocol;
            return false;
        }

        if (value.StartsWith(TcpPrefix, StringComparison.Ordinal))
        {
            value = value[TcpPrefix.Length..];
        }

        string? port = null;
        var comma = value.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0)
        {
            port = value[(comma + 1)..];
            value = value[..comma];

            if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || number is < 1 or > 65535)
            {
                error = ConnectionAddressError.InvalidPort;
                return false;
            }

            port = number.ToString(CultureInfo.InvariantCulture);
        }

        string? instance = null;
        var slash = value.IndexOf('\\', StringComparison.Ordinal);
        if (slash >= 0)
        {
            instance = value[(slash + 1)..];
            value = value[..slash];
        }

        var host = value.TrimEnd('.');
        if (host.Length == 0 || instance is { Length: 0 })
        {
            error = ConnectionAddressError.Empty;
            return false;
        }

        var result = host;
        if (instance is not null)
        {
            result += "\\" + instance;
        }

        if (port is not null && port != DefaultPort)
        {
            result += "," + port;
        }

        if (result.Length > ServerMaxLength)
        {
            error = ConnectionAddressError.TooLong;
            return false;
        }

        normalized = result;
        error = ConnectionAddressError.None;
        return true;
    }

    /// <summary>
    /// Base normalizada: solo sin espacios al inicio y al final. Se conservan las mayúsculas porque en una
    /// instancia con intercalación sensible a mayúsculas el nombre exacto importa; la unicidad sin distinguir
    /// mayúsculas la da el índice sobre lower(database_name).
    /// </summary>
    public static bool TryNormalizeDatabaseName(string? input, out string normalized, out ConnectionAddressError error)
    {
        normalized = (input ?? string.Empty).Trim();

        if (normalized.Length == 0)
        {
            error = ConnectionAddressError.Empty;
            return false;
        }

        if (normalized.Length > DatabaseNameMaxLength)
        {
            normalized = string.Empty;
            error = ConnectionAddressError.TooLong;
            return false;
        }

        error = ConnectionAddressError.None;
        return true;
    }

    /// <summary>Clave de unicidad de la base, igual a lower(database_name) en PostgreSQL para nombres ASCII.</summary>
    public static string DatabaseNameKey(string normalizedDatabaseName) =>
        normalizedDatabaseName.ToLowerInvariant();
}
