using Microsoft.Extensions.Configuration;
using QueryDoctor.Infrastructure.Connections;

namespace QueryDoctor.UnitTests.Connections;

/// <summary>P9 + auditoría B6: la excepción de TLS del laboratorio solo arranca en el laboratorio (invariante 3).</summary>
public class LabCertificateGuardTests
{
    // Las mismas claves que pone docker-compose.lab.yml como variables de entorno (Probe__...__0).
    private static IConfiguration Config(string? appEnv, params string[] servers)
    {
        var values = servers
            .Select((s, i) => new KeyValuePair<string, string?>($"Probe:LabTrustedCertificateServers:{i}", s))
            .ToList();
        if (appEnv is not null)
        {
            values.Add(new KeyValuePair<string, string?>("APP_ENV", appEnv));
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Theory]
    [InlineData("Development", null)]
    [InlineData("development", "")]
    [InlineData("Development", "Development")]
    public void EnElLaboratorioLaExcepcionSeAceptaYSeLee(string environment, string? appEnv)
    {
        var servers = LabCertificateGuard.EnsureAllowed(Config(appEnv, "sqlserver"), environment);

        Assert.Equal(["sqlserver"], servers);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("prod")]
    [InlineData("Staging")]
    [InlineData("")]
    public void FueraDeDevelopmentLaExcepcionImpideArrancar(string environment)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LabCertificateGuard.EnsureAllowed(Config(null, "sqlserver"), environment));

        Assert.Contains("invariante 3", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sqlserver", ex.Message, StringComparison.Ordinal);
    }

    // Auditoría B6: aunque DOTNET_ENVIRONMENT diga Development (valor por defecto del compose), APP_ENV=prod gana.
    [Theory]
    [InlineData("prod")]
    [InlineData("PROD")]
    [InlineData(" prod ")]
    public void ConAppEnvProdLaExcepcionImpideArrancarAunqueElEntornoDigaDevelopment(string appEnv)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LabCertificateGuard.EnsureAllowed(Config(appEnv, "sqlserver"), "Development"));

        Assert.Contains("APP_ENV=prod", ex.Message, StringComparison.Ordinal);
    }

    // Auditoría B6: la excepción no se puede apuntar a otro servidor (p. ej. uno real con certificado vencido).
    [Theory]
    [InlineData("sql.empresa.com")]
    [InlineData("SQLSERVER")]
    [InlineData("sqlserver,1433")]
    public void SoloSeAdmiteElContenedorDeLaboratorio(string server)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => LabCertificateGuard.EnsureAllowed(Config(null, "sqlserver", server), "Development"));

        Assert.Contains("contenedor de laboratorio", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(server, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", "prod")]
    [InlineData("Development", null)]
    public void SinExcepcionArrancaEnCualquierEntorno(string environment, string? appEnv)
    {
        Assert.Empty(LabCertificateGuard.EnsureAllowed(Config(appEnv), environment));
    }

    [Fact]
    public void UnValorEnBlancoNoCuentaComoExcepcion()
    {
        Assert.Empty(LabCertificateGuard.EnsureAllowed(Config("prod", "  "), "Production"));
    }
}
