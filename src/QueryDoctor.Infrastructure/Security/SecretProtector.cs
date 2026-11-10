using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using QueryDoctor.Application.Common.Security;

namespace QueryDoctor.Infrastructure.Security;

/// <summary>
/// Implementación de <see cref="IFieldEncryptor"/> que proporciona cifrado por sobre (envelope)
/// utilizando AES-256-GCM para datos en reposo (RNF-SEG-04, RNF-SEG-14, ADR-11).
/// </summary>
public sealed class SecretProtector : IFieldEncryptor, IDisposable
{
    private const byte CurrentEnvelopeVersion = 0x01;
    private const int KeySizeBytes = 32;       // 256 bits
    private const int NonceSizeBytes = 12;     // 96 bits recomendado por NIST SP 800-38D
    private const int TagSizeBytes = 16;       // 128 bits
    private const int HeaderSizeBytes = 1 + NonceSizeBytes + TagSizeBytes; // 29 bytes

    private static readonly byte[] VersionAad = [CurrentEnvelopeVersion];

    private readonly AesGcm _aesGcm;
    private bool _disposed;

    /// <summary>
    /// Inicializa una instancia de <see cref="SecretProtector"/> utilizando opciones de configuración.
    /// </summary>
    /// <param name="options">Opciones con la clave o ruta al secreto.</param>
    public SecretProtector(IOptions<SecretProtectorOptions> options)
        : this(ResolveMasterKey(options?.Value))
    {
    }

    /// <summary>
    /// Inicializa una instancia de <see cref="SecretProtector"/> a partir de una clave maestra en Base64 o Hexadecimal.
    /// </summary>
    /// <param name="masterKeyBase64OrHex">Clave maestra de 256 bits (32 bytes).</param>
    public SecretProtector(string masterKeyBase64OrHex)
        : this(ParseKey(masterKeyBase64OrHex))
    {
    }

    /// <summary>
    /// Inicializa una instancia de <see cref="SecretProtector"/> con la clave maestra en bytes crudos.
    /// </summary>
    /// <param name="masterKeyBytes">Clave maestra de 32 bytes (256 bits).</param>
    public SecretProtector(byte[] masterKeyBytes)
    {
        ArgumentNullException.ThrowIfNull(masterKeyBytes);

        if (masterKeyBytes.Length != KeySizeBytes)
        {
            throw new ArgumentException(
                $"La clave maestra debe tener exactamente {KeySizeBytes} bytes (256 bits). Longitud recibida: {masterKeyBytes.Length} bytes.",
                nameof(masterKeyBytes));
        }

        _aesGcm = new AesGcm(masterKeyBytes, TagSizeBytes);
    }

    /// <inheritdoc />
    public string Encrypt(string plainText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(plainText);

        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes = Encrypt(plainBytes);

        return Convert.ToBase64String(cipherBytes);
    }

    /// <inheritdoc />
    public string Decrypt(string cipherText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cipherText);

        byte[] cipherBytes;
        try
        {
            cipherBytes = Convert.FromBase64String(cipherText);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("El criptograma no es una cadena Base64 válida.", ex);
        }

        byte[] plainBytes = Decrypt(cipherBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <inheritdoc />
    public byte[] Encrypt(ReadOnlySpan<byte> plainBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] result = new byte[HeaderSizeBytes + plainBytes.Length];

        // Encabezado: [Version (1B)]
        result[0] = CurrentEnvelopeVersion;

        // [Nonce (12B)]
        Span<byte> nonceSpan = result.AsSpan(1, NonceSizeBytes);
        RandomNumberGenerator.Fill(nonceSpan);

        // [Tag (16B)] y [Ciphertext (NB)]
        Span<byte> tagSpan = result.AsSpan(1 + NonceSizeBytes, TagSizeBytes);
        Span<byte> cipherSpan = result.AsSpan(HeaderSizeBytes, plainBytes.Length);

        // VersionAad se usa como datos asociados para garantizar integridad del encabezado
        _aesGcm.Encrypt(nonceSpan, plainBytes, cipherSpan, tagSpan, VersionAad);

        return result;
    }

    /// <inheritdoc />
    public byte[] Decrypt(ReadOnlySpan<byte> cipherBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (cipherBytes.Length < HeaderSizeBytes)
        {
            throw new CryptographicException(
                $"El criptograma recibido tiene una longitud menor a la requerida por el sobre ({HeaderSizeBytes} bytes).");
        }

        byte version = cipherBytes[0];
        if (version != CurrentEnvelopeVersion)
        {
            throw new CryptographicException(
                $"Versión de sobre criptográfico no compatible: 0x{version:X2}. Versión soportada: 0x{CurrentEnvelopeVersion:X2}.");
        }

        ReadOnlySpan<byte> nonceSpan = cipherBytes.Slice(1, NonceSizeBytes);
        ReadOnlySpan<byte> tagSpan = cipherBytes.Slice(1 + NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> ciphertextSpan = cipherBytes.Slice(HeaderSizeBytes);

        byte[] plainBytes = new byte[ciphertextSpan.Length];

        try
        {
            _aesGcm.Decrypt(nonceSpan, ciphertextSpan, tagSpan, plainBytes, VersionAad);
        }
        catch (CryptographicException ex)
        {
            // Mensaje genérico para no filtrar información ni claves en logs (RNF-SEG-04, RNF-SEG-09)
            throw new CryptographicException(
                "Fallo al descifrar el campo: datos manipulados, etiqueta de autenticación no válida o clave errónea.",
                ex);
        }

        return plainBytes;
    }

    private static byte[] ResolveMasterKey(SecretProtectorOptions? options)
    {
        // 1. Opciones en memoria / configuración explícita
        if (!string.IsNullOrWhiteSpace(options?.MasterKey))
        {
            return ParseKey(options.MasterKey);
        }

        // 2. Archivo configurado en MasterKeyPath (ej. Docker secret)
        if (!string.IsNullOrWhiteSpace(options?.MasterKeyPath) && File.Exists(options.MasterKeyPath))
        {
            string fileContent = File.ReadAllText(options.MasterKeyPath).Trim();
            return ParseKey(fileContent);
        }

        // 3. Variable de entorno MASTER_ENCRYPTION_KEY
        string? envKey = Environment.GetEnvironmentVariable("MASTER_ENCRYPTION_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return ParseKey(envKey.Trim());
        }

        // 4. Ruta estándar de Docker secret en Linux / contenedor
        const string defaultSecretPath = "/run/secrets/master_encryption_key";
        if (File.Exists(defaultSecretPath))
        {
            string secretContent = File.ReadAllText(defaultSecretPath).Trim();
            return ParseKey(secretContent);
        }

        throw new InvalidOperationException(
            "No se encontró una clave maestra de cifrado. Configure 'Security:SecretProtector:MasterKey', " +
            "la variable de entorno 'MASTER_ENCRYPTION_KEY' o el secreto '/run/secrets/master_encryption_key'.");
    }

    private static byte[] ParseKey(string keyString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyString);

        // Intento 1: Hexadecimal (64 caracteres = 32 bytes)
        if (keyString.Length == KeySizeBytes * 2 && IsHex(keyString))
        {
            return Convert.FromHexString(keyString);
        }

        // Intento 2: Base64
        try
        {
            byte[] bytes = Convert.FromBase64String(keyString);
            if (bytes.Length == KeySizeBytes)
            {
                return bytes;
            }
        }
        catch (FormatException)
        {
            // No es Base64
        }

        throw new ArgumentException(
            $"La clave maestra no es válida. Debe ser una cadena de {KeySizeBytes} bytes codificada en Base64 o Hexadecimal (64 caracteres).");
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _aesGcm.Dispose();
            _disposed = true;
        }
    }
}
