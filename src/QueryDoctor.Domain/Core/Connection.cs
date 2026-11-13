namespace QueryDoctor.Domain.Core;

/// <summary>
/// Conexión registrada hacia una base de datos objetivo (RF-CFG-01). Tabla core.connection.
/// </summary>
public sealed class Connection
{
    public Guid Id { get; set; }

    public ConnectionPlatform Platform { get; set; }

    public string Server { get; set; } = string.Empty;

    public string DatabaseName { get; set; } = string.Empty;

    public AuthenticationMethod AuthenticationMethod { get; set; }

    public ConnectionEncryption Encryption { get; set; } = ConnectionEncryption.Mandatory;

    /// <summary>
    /// Credenciales YA cifradas con IFieldEncryptor (RNF-SEG-04, RNF-SEG-14).
    /// Quien guarda este campo cifra antes; esta capa nunca ve el texto en claro.
    /// </summary>
    public string? EncryptedCredentials { get; set; }

    public ConnectionStatus Status { get; set; } = ConnectionStatus.Active;

    /// <summary>Código del último fallo de sondeo; null si el último sondeo fue correcto o no ha corrido.</summary>
    public ProbeFailureCause? ProbeFailureCause { get; set; }

    /// <summary>Cuándo terminó el último sondeo; null mientras está pendiente.</summary>
    public DateTimeOffset? LastProbedAt { get; set; }

    /// <summary>
    /// Marca de agua: último runtime_stats_interval_id consolidado (RF-ING-05).
    /// Se avanza en la misma transacción del upsert de la ingesta.
    /// </summary>
    public long? LastConsolidatedIntervalId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
