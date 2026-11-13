using System.Globalization;
using System.Text.Json;
using QueryDoctor.Domain.Core;

namespace QueryDoctor.Application.Connections;

/// <summary>Fila del listado GET /connections (docs/CONTRATO_API_CONEXIONES.md). Nunca lleva credenciales.</summary>
public sealed record ConnectionSummary(
    Guid Id,
    ConnectionPlatform Platform,
    string Server,
    string DatabaseName,
    ConnectionStatus Status,
    ProbeFailureCause? FailureCause,
    string? FailureMessage,
    SupportLevel? SupportLevel,
    string? EngineVersion,
    string? QueryStoreState,
    bool? HasExcessivePermissions,
    DateTimeOffset? LastProbedAt,
    DateTimeOffset? LastIngestionAt);

/// <summary>Detalle GET /connections/{id}: estado del sondeo y perfil de capacidades.</summary>
public sealed record ConnectionDetail(
    Guid Id,
    ConnectionPlatform Platform,
    string Server,
    string DatabaseName,
    ConnectionStatus Status,
    ProbeFailureCause? FailureCause,
    string? FailureMessage,
    string MinimumSupportedVersion,
    DateTimeOffset? LastProbedAt,
    DateTimeOffset? LastIngestionAt,
    CapabilitiesView? Capabilities);

/// <summary>Perfil de capacidades (RF-CFG-03, 04, 07; RNF-COM-04).</summary>
public sealed record CapabilitiesView(
    string EngineVersion,
    string? Edition,
    SupportLevel SupportLevel,
    bool? HasExcessivePermissions,
    QueryStoreView QueryStore,
    IReadOnlyList<OmittedFeatureView> OmittedFeatures);

/// <summary>Estado de Query Store tal como se leyó, sin modificarlo (RF-CFG-04, CA-HU02.1, CA-HU02.3).</summary>
public sealed record QueryStoreView(
    string? ActualState,
    string? DesiredState,
    int? ReadOnlyReason,
    int? IntervalLengthMinutes,
    string? CaptureMode,
    int? RetentionDays,
    bool CanIngest);

public sealed record OmittedFeatureView(OmittedFeature Code, string Message);

/// <summary>Consultas de lectura de conexiones. En S2-12 se agrega el filtro por conexión asignada en la consulta.</summary>
public interface IConnectionQueries
{
    Task<IReadOnlyList<ConnectionSummary>> ListAsync(CancellationToken cancellationToken);

    Task<ConnectionDetail?> GetAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Arma las vistas a partir de las entidades. Función pura, sin E/S.</summary>
public static class ConnectionViewMapper
{
    public static ConnectionSummary ToSummary(Connection connection, CapabilityProfile? profile, DateTimeOffset? lastIngestionAt)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var visible = VisibleProfile(connection, profile);

        return new ConnectionSummary(
            connection.Id,
            connection.Platform,
            connection.Server,
            connection.DatabaseName,
            connection.Status,
            connection.ProbeFailureCause,
            Message(connection.ProbeFailureCause),
            visible?.SupportLevel,
            visible?.EngineVersion,
            visible?.QueryStoreActualState,
            HasExcessivePermissions: null,
            connection.LastProbedAt,
            lastIngestionAt);
    }

    public static ConnectionDetail ToDetail(Connection connection, CapabilityProfile? profile, DateTimeOffset? lastIngestionAt)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var visible = VisibleProfile(connection, profile);

        return new ConnectionDetail(
            connection.Id,
            connection.Platform,
            connection.Server,
            connection.DatabaseName,
            connection.Status,
            connection.ProbeFailureCause,
            Message(connection.ProbeFailureCause),
            ProbeFailureMessages.MinimumSupportedVersion,
            connection.LastProbedAt,
            lastIngestionAt,
            visible is null ? null : ToCapabilities(visible));
    }

    // El perfil solo vale si el último sondeo fue correcto: tras un reemplazo (D3) puede quedar uno viejo.
    private static CapabilityProfile? VisibleProfile(Connection connection, CapabilityProfile? profile) =>
        connection.Status is ConnectionStatus.Active or ConnectionStatus.Disabled ? profile : null;

    private static CapabilitiesView ToCapabilities(CapabilityProfile profile) => new(
        profile.EngineVersion,
        profile.Edition,
        profile.SupportLevel,
        // RF-CFG-05 va en el Sprint 2: null = "no evaluado", nunca "la cuenta está bien".
        HasExcessivePermissions: null,
        new QueryStoreView(
            profile.QueryStoreActualState,
            profile.QueryStoreDesiredState,
            int.TryParse(profile.QueryStoreReadOnlyReason, NumberStyles.Integer, CultureInfo.InvariantCulture, out var reason) ? reason : null,
            profile.IntervalLengthMinutes,
            profile.CaptureMode,
            profile.RetentionDays,
            QueryStoreState.CanIngest(profile.QueryStoreActualState)),
        ParseOmitted(profile.OmittedFeaturesJson));

    private static List<OmittedFeatureView> ParseOmitted(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var codes = JsonSerializer.Deserialize<string[]>(json) ?? [];
        return codes
            .Select(code => Enum.TryParse<OmittedFeature>(code, out var feature) ? feature : (OmittedFeature?)null)
            .OfType<OmittedFeature>()
            .Select(feature => new OmittedFeatureView(feature, SupportMatrix.MessageFor(feature)))
            .ToList();
    }

    private static string? Message(ProbeFailureCause? cause) =>
        cause is null ? null : ProbeFailureMessages.For(cause.Value);
}
