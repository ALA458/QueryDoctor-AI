using QueryDoctor.Domain.Core;

namespace QueryDoctor.UnitTests.Connections;

// S1-09: normalización de servidor y base antes de guardar y de buscar duplicados (RF-CFG-01).
public class ConnectionAddressTests
{
    [Theory]
    // Filas pedidas en la aprobación de P2.
    [InlineData("tcp:SRV,1433", "srv")]
    [InlineData("srv,14330", "srv,14330")]
    [InlineData(@"srv\Inst", @"srv\inst")]
    [InlineData("srv.database.windows.net.", "srv.database.windows.net")]
    // Bordes adicionales.
    [InlineData("TCP:Srv.Database.Windows.Net,1433", "srv.database.windows.net")]
    [InlineData("  SRV01 , 1433 ", "srv01")]
    [InlineData("sqlserver,1433", "sqlserver")]
    [InlineData(@"SRV.\INST,1500", @"srv\inst,1500")]
    [InlineData("10.0.0.5", "10.0.0.5")]
    [InlineData("srv,01433", "srv")]
    public void NormalizaElServidor(string input, string expected)
    {
        Assert.True(ConnectionAddress.TryNormalizeServer(input, out var normalized, out var error));
        Assert.Equal(ConnectionAddressError.None, error);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("np:srv")]
    [InlineData("NP:srv")]
    [InlineData("lpc:srv")]
    [InlineData("admin:srv")]
    [InlineData(" Admin:srv,1433")]
    public void RechazaProtocolosQueCambianElTransporteOAbrenLaDac(string input)
    {
        Assert.False(ConnectionAddress.TryNormalizeServer(input, out var normalized, out var error));
        Assert.Equal(ConnectionAddressError.ForbiddenProtocol, error);
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData("srv,abc")]
    [InlineData("srv,0")]
    [InlineData("srv,65536")]
    [InlineData("srv,")]
    [InlineData("srv,-1")]
    public void RechazaPuertosInvalidos(string input)
    {
        Assert.False(ConnectionAddress.TryNormalizeServer(input, out _, out var error));
        Assert.Equal(ConnectionAddressError.InvalidPort, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tcp:")]
    [InlineData(",1433")]
    [InlineData(@"srv\")]
    [InlineData("...")]
    public void RechazaServidorVacio(string? input)
    {
        Assert.False(ConnectionAddress.TryNormalizeServer(input, out _, out var error));
        Assert.Equal(ConnectionAddressError.Empty, error);
    }

    [Fact]
    public void RechazaServidorDemasiadoLargo()
    {
        Assert.False(ConnectionAddress.TryNormalizeServer(new string('a', 256), out _, out var error));
        Assert.Equal(ConnectionAddressError.TooLong, error);
    }

    [Fact]
    public void LaBaseConservaMayusculasYEspaciosInternos()
    {
        // En una intercalación sensible a mayúsculas "QD_Lab" y "qd_lab" son bases distintas para el motor.
        Assert.True(ConnectionAddress.TryNormalizeDatabaseName("  QD_Lab Ventas ", out var normalized, out _));
        Assert.Equal("QD_Lab Ventas", normalized);
    }

    [Fact]
    public void LaClaveDeUnicidadDeLaBaseNoDistingueMayusculas()
    {
        Assert.Equal(
            ConnectionAddress.DatabaseNameKey("QD_Lab"),
            ConnectionAddress.DatabaseNameKey("qd_LAB"));
    }

    [Theory]
    [InlineData(null, ConnectionAddressError.Empty)]
    [InlineData("  ", ConnectionAddressError.Empty)]
    public void RechazaBaseVacia(string? input, ConnectionAddressError expected)
    {
        Assert.False(ConnectionAddress.TryNormalizeDatabaseName(input, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void RechazaBaseDemasiadoLarga()
    {
        Assert.False(ConnectionAddress.TryNormalizeDatabaseName(new string('b', 129), out _, out var error));
        Assert.Equal(ConnectionAddressError.TooLong, error);
    }

    [Fact]
    public void CadaCausaDeFalloTieneUnMensajeFijoPropio()
    {
        var messages = Enum.GetValues<ProbeFailureCause>().Select(ProbeFailureMessages.For).ToList();

        Assert.Equal(messages.Count, messages.Distinct().Count());
        Assert.Contains("SQL Server 2016 (13.x)", ProbeFailureMessages.For(ProbeFailureCause.UnsupportedVersion), StringComparison.Ordinal);
    }
}
