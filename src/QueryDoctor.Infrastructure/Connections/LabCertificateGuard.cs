using Microsoft.Extensions.Configuration;
using QueryDoctor.Application.Connections;

namespace QueryDoctor.Infrastructure.Connections;

/// <summary>
/// Candado de la excepción de TLS del laboratorio (invariante 3, RNF-SEG-03). Si
/// <c>Probe:LabTrustedCertificateServers</c> tiene algún servidor y el entorno no es Development, el Worker no arranca.
/// La excepción solo la activa <c>docker-compose.lab.yml</c>; el compose principal no la tiene.
/// </summary>
public static class LabCertificateGuard
{
    private const string DevelopmentEnvironment = "Development";

    /// <summary>Lee la sección Probe y falla al arrancar si la excepción está puesta fuera de Development.</summary>
    /// <returns>Los servidores de la excepción (vacío si no hay).</returns>
    public static IReadOnlyList<string> EnsureAllowed(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(ProbeOptions.SectionName).Get<ProbeOptions>() ?? new ProbeOptions();
        var servers = options.LabTrustedCertificateServers
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (servers.Count > 0 && !string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            // Solo el nombre del entorno: la lista de servidores no se necesita para corregirlo.
            throw new InvalidOperationException(
                $"Probe:LabTrustedCertificateServers solo se permite en Development (invariante 3, RNF-SEG-03); " +
                $"entorno actual: '{environmentName}'. Quita docker-compose.lab.yml o la variable.");
        }

        return servers;
    }
}
