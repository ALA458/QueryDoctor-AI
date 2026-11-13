using System.ComponentModel;
using System.Net.Sockets;
using System.Security.Authentication;
using QueryDoctor.Application.Telemetry;

namespace QueryDoctor.Infrastructure.Telemetry;

/// <summary>
/// Traduce un fallo de SqlClient a <see cref="TelemetryFailureKind"/> por número de error y por la cadena de
/// InnerException (TLS, sockets), sin leer ni copiar el texto del mensaje (S1-09, RNF-SEG-09).
/// Recibe el número aparte para poder probarlo sin una SqlException real (no tiene constructor público).
/// </summary>
public static class SqlErrorClassifier
{
    // SEC_E_UNTRUSTED_ROOT, CERT_E_CN_NO_MATCH, SEC_E_WRONG_PRINCIPAL, CERT_E_EXPIRED (Windows / SChannel).
    private static readonly HashSet<int> TlsNativeErrors =
    [
        unchecked((int)0x80090325),
        unchecked((int)0x800B010F),
        unchecked((int)0x80090322),
        unchecked((int)0x800B0101),
    ];

    public static TelemetryFailureKind Classify(int? sqlErrorNumber, Exception? exception)
    {
        // Primero la causa de transporte: un certificado rechazado llega como SqlException número 0 o 20.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return TelemetryFailureKind.TlsCertificateInvalid;
            }

            if (current is Win32Exception win32 && TlsNativeErrors.Contains(win32.NativeErrorCode))
            {
                return TelemetryFailureKind.TlsCertificateInvalid;
            }

            if (current is TimeoutException)
            {
                return TelemetryFailureKind.Timeout;
            }
        }

        var byNumber = sqlErrorNumber switch
        {
            // Inicio de sesión rechazado, cuenta deshabilitada o bloqueada, contraseña caducada.
            18452 or 18456 or 18470 or 18486 or 18487 or 18488 => TelemetryFailureKind.InvalidCredentials,

            // Sin acceso a la base (4060) o permiso denegado sobre vistas de sistema y DMV.
            4060 or 229 or 230 or 262 or 297 or 300 or 916 => TelemetryFailureKind.PermissionDenied,

            // Tiempo de espera de SqlClient.
            -2 => TelemetryFailureKind.Timeout,

            // Red, instancia no encontrada, Azure SQL no disponible.
            2 or 26 or 40 or 53 or 121 or 1225 or 10053 or 10054 or 10060 or 10061 or 11001 or 11004
                or 40613 or 40197 or 40501 => TelemetryFailureKind.ServerUnreachable,

            _ => (TelemetryFailureKind?)null,
        };

        if (byNumber is not null)
        {
            return byNumber.Value;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException)
            {
                return TelemetryFailureKind.ServerUnreachable;
            }
        }

        return TelemetryFailureKind.Unknown;
    }
}
