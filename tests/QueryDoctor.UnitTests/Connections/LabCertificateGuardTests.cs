using Microsoft.Extensions.Configuration;
using QueryDoctor.Infrastructure.Connections;

namespace QueryDoctor.UnitTests.Connections;

/// <summary>P9: la excepción de TLS del laboratorio solo arranca en Development (invariante 3, RNF-SEG-03).</summary>
public class LabCertificateGuardTests
{
    // Las mismas claves que pone docker-compose.lab.yml como variables de entorno (Probe__...__0).
    private static IConfiguration Config(params string[] servers) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(servers.Select((s, i) =>
                new KeyValuePair<string, string?>($"Probe:LabTrustedCertificateServers:{i}", s)))
            .Build();

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    public void EnDevelopmentLaExcepcionSeAceptaYSeLee(string environment)
    {
        var servers = LabCertificateGuard.EnsureAllowed(Config("sqlserver"), environment);

        Assert.Equal(["sqlserver"], servers);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("prod")]
    [InlineData("Staging")]
    [InlineData("")]
    public void FueraDeDevelopmentLaExcepcionImpideArrancar(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => LabCertificateGuard.EnsureAllowed(Config("sqlserver"), environment));

        Assert.Contains("invariante 3", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sqlserver", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void SinExcepcionArrancaEnCualquierEntorno(string environment)
    {
        Assert.Empty(LabCertificateGuard.EnsureAllowed(Config(), environment));
    }

    [Fact]
    public void UnValorEnBlancoNoCuentaComoExcepcion()
    {
        Assert.Empty(LabCertificateGuard.EnsureAllowed(Config("  "), "Production"));
    }
}
