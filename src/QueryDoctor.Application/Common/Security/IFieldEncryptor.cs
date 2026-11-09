namespace QueryDoctor.Application.Common.Security;

/// <summary>
/// Contrato para el cifrado y descifrado de campos sensibles en reposo (RNF-SEG-04, RNF-SEG-14).
/// </summary>
public interface IFieldEncryptor
{
    /// <summary>
    /// Cifra una cadena de texto en claro usando AES-256-GCM.
    /// </summary>
    /// <param name="plainText">Texto en claro a cifrar.</param>
    /// <returns>Criptograma codificado en Base64 con encabezado de versión, nonce y tag de autenticación.</returns>
    string Encrypt(string plainText);

    /// <summary>
    /// Descifra un criptograma en Base64 producido por <see cref="Encrypt(string)"/>.
    /// </summary>
    /// <param name="cipherText">Criptograma en Base64.</param>
    /// <returns>Texto en claro original.</returns>
    string Decrypt(string cipherText);

    /// <summary>
    /// Cifra una secuencia de bytes en claro usando AES-256-GCM.
    /// </summary>
    /// <param name="plainBytes">Bytes a cifrar.</param>
    /// <returns>Arreglo de bytes cifrado conteniendo versión (1B), nonce (12B), tag (16B) y texto cifrado.</returns>
    byte[] Encrypt(ReadOnlySpan<byte> plainBytes);

    /// <summary>
    /// Descifra una secuencia de bytes previamente cifrada con <see cref="Encrypt(ReadOnlySpan{byte})"/>.
    /// </summary>
    /// <param name="cipherBytes">Bytes cifrados.</param>
    /// <returns>Bytes originales en claro.</returns>
    byte[] Decrypt(ReadOnlySpan<byte> cipherBytes);
}
