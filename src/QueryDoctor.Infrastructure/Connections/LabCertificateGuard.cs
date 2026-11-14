using Microsoft.Extensions.Configuration;
using QueryDoctor.Application.Connections;

namespace QueryDoctor.Infrastructure.Connections;

/// <summary>
/// Candado de la excepción de TLS del laboratorio (invariante 3, RNF-SEG-03). Si
/// <c>Probe:LabTrustedCertificateServers</c> tiene algún servidor, el Worker no arranca salvo que se cumpla TODO:
/// entorno <c>Development</c>, <c>APP_ENV</c> distinto de <c>prod</c> (mismo criterio que el candado de ADR-10) y
/// solo el contenedor de laboratorio <c>sqlserver</c> en la lista. La activa únicamente <c>docker-compose.lab.yml</c>.
/// </summary>
public static class LabCertificateGuard
{
    /// <summary>Único servidor admitido: el servicio sqlserver del compose (red targets).</summary>
    public const string LabServer = "sqlserver";

    private const string DevelopmentEnvironment = "Development";
    private const string ProductionAppEnv = "prod";

    /// <summary>Lee la sección Probe y falla al arrancar si la excepción no está permitida.</summary>
    /// <returns>Los servidores de la excepción (vacío si no hay).</returns>
    public static IReadOnlyList<string> EnsureAllowed(IConfiguration configuration, string environmentName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration.GetSection(ProbeOptions.SectionName).Get<ProbeOptions>() ?? new ProbeOptions();
        var servers = options.LabTrustedCertificateServers
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (servers.Count == 0)
        {
            return servers;
        }

        // Los mensajes llevan solo el entorno o la regla: la lista de servidores no hace falta para corregirlo.
        if (!string.Equals(environmentName, DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            throw Refused($"solo se permite en Development; entorno actual: '{environmentName}'");
        }

        // Auditoría B6: el compose pone Development si falta APP_ENV; APP_ENV=prod gana siempre.
        if (string.Equals(configuration["APP_ENV"]?.Trim(), ProductionAppEnv, StringComparison.OrdinalIgnoreCase))
        {
            throw Refused("APP_ENV=prod");
        }

        if (servers.Any(s => !string.Equals(s.Trim(), LabServer, StringComparison.Ordinal)))
        {
            throw Refused($"solo admite el contenedor de laboratorio '{LabServer}'");
        }

        return servers;
    }

    private static InvalidOperationException Refused(string reason) =>
        new($"Probe:LabTrustedCertificateServers rechazada (invariante 3, RNF-SEG-03): {reason}. " +
            "Quita docker-compose.lab.yml o la variable.");
}
