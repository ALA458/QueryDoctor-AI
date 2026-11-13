using System.Text.Json;
using QueryDoctor.Application.Common.Security;

namespace QueryDoctor.Application.Connections;

/// <summary>
/// Usuario y contraseña de autenticación SQL hacia la base objetivo. Solo existe en memoria: se guarda cifrado
/// con <see cref="IFieldEncryptor"/> (RNF-SEG-04, RNF-SEG-14) y su ToString no muestra la contraseña (RNF-SEG-09).
/// </summary>
public sealed record ConnectionCredentials(string UserName, string Password)
{
    public override string ToString() => $"ConnectionCredentials {{ UserName = {UserName}, Password = ******** }}";
}

/// <summary>Cifra y descifra las credenciales como un único campo encrypted_credentials.</summary>
public static class ConnectionCredentialsCodec
{
    public static string Encrypt(ConnectionCredentials credentials, IFieldEncryptor encryptor)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(encryptor);

        var json = JsonSerializer.Serialize(new Payload(credentials.UserName, credentials.Password));
        return encryptor.Encrypt(json);
    }

    public static ConnectionCredentials Decrypt(string encryptedCredentials, IFieldEncryptor encryptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedCredentials);
        ArgumentNullException.ThrowIfNull(encryptor);

        var payload = JsonSerializer.Deserialize<Payload>(encryptor.Decrypt(encryptedCredentials))
            ?? throw new InvalidOperationException("Las credenciales cifradas están vacías.");

        return new ConnectionCredentials(payload.U, payload.P);
    }

    // Nombres cortos: es un formato interno, versionado por el propio sobre de SecretProtector.
    private sealed record Payload(string U, string P);
}
