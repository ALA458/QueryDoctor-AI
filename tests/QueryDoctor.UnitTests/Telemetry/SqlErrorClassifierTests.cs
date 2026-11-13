using System.ComponentModel;
using System.Net.Sockets;
using System.Security.Authentication;
using QueryDoctor.Application.Telemetry;
using QueryDoctor.Infrastructure.Telemetry;

namespace QueryDoctor.UnitTests.Telemetry;

// S1-09, punto 3 de la aprobación: traducir por número de error e InnerException, sin leer el mensaje.
public class SqlErrorClassifierTests
{
    [Theory]
    [InlineData(18456, TelemetryFailureKind.InvalidCredentials)] // Login failed
    [InlineData(18470, TelemetryFailureKind.InvalidCredentials)] // cuenta deshabilitada
    [InlineData(18488, TelemetryFailureKind.InvalidCredentials)] // contraseña debe cambiarse
    [InlineData(4060, TelemetryFailureKind.PermissionDenied)]    // no puede abrir la base
    [InlineData(229, TelemetryFailureKind.PermissionDenied)]
    [InlineData(262, TelemetryFailureKind.PermissionDenied)]     // F4(b) en el laboratorio
    [InlineData(300, TelemetryFailureKind.PermissionDenied)]     // F6 en el laboratorio
    [InlineData(-2, TelemetryFailureKind.Timeout)]
    [InlineData(53, TelemetryFailureKind.ServerUnreachable)]
    [InlineData(26, TelemetryFailureKind.ServerUnreachable)]
    [InlineData(40613, TelemetryFailureKind.ServerUnreachable)]  // Azure: base no disponible
    [InlineData(208, TelemetryFailureKind.Unknown)]              // objeto inexistente
    [InlineData(null, TelemetryFailureKind.Unknown)]
    public void ClasificaPorNumeroDeError(int? number, TelemetryFailureKind expected)
    {
        Assert.Equal(expected, SqlErrorClassifier.Classify(number, new InvalidOperationException("texto del motor")));
    }

    [Fact]
    public void UnCertificadoRechazadoEnLinuxEsTls()
    {
        var ex = new InvalidOperationException("x", new AuthenticationException("remote certificate invalid"));

        Assert.Equal(TelemetryFailureKind.TlsCertificateInvalid, SqlErrorClassifier.Classify(0, ex));
    }

    [Fact]
    public void UnaRaizNoConfiableEnWindowsEsTls()
    {
        var ex = new InvalidOperationException("x", new Win32Exception(unchecked((int)0x80090325)));

        Assert.Equal(TelemetryFailureKind.TlsCertificateInvalid, SqlErrorClassifier.Classify(20, ex));
    }

    [Fact]
    public void TlsGanaAlNumeroDeError()
    {
        // SqlClient puede envolver el fallo de TLS en un error de red; la causa real es el certificado.
        var ex = new InvalidOperationException("x", new AuthenticationException("y"));

        Assert.Equal(TelemetryFailureKind.TlsCertificateInvalid, SqlErrorClassifier.Classify(10054, ex));
    }

    [Fact]
    public void UnSocketSinNumeroConocidoEsServidorInalcanzable()
    {
        var ex = new InvalidOperationException("x", new SocketException((int)SocketError.HostNotFound));

        Assert.Equal(TelemetryFailureKind.ServerUnreachable, SqlErrorClassifier.Classify(0, ex));
    }

    [Fact]
    public void UnTimeoutInternoEsTimeout()
    {
        Assert.Equal(TelemetryFailureKind.Timeout, SqlErrorClassifier.Classify(0, new TimeoutException()));
    }

    [Fact]
    public void LaExcepcionTipadaNoArrastraElTextoNiLaExcepcionOriginal()
    {
        var ex = new TelemetrySourceException(TelemetryFailureKind.InvalidCredentials, 18456);

        Assert.Null(ex.InnerException);
        Assert.Equal("Fallo de telemetría: InvalidCredentials (error 18456)", ex.Message);
    }
}
