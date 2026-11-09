using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using QueryDoctor.Infrastructure.Security;

namespace QueryDoctor.UnitTests.Security;

public class SecretProtectorTests : IDisposable
{
    // Clave de prueba de 256 bits (32 bytes)
    private static readonly byte[] ValidKeyBytes = [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    private static readonly byte[] AnotherKeyBytes = [
        0xFF, 0xEE, 0xDD, 0xCC, 0xBB, 0xAA, 0x99, 0x88,
        0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11, 0x00,
        0x0F, 0x1E, 0x2D, 0x3C, 0x4B, 0x5A, 0x69, 0x78,
        0x87, 0x96, 0xA5, 0xB4, 0xC3, 0xD2, 0xE1, 0xF0
    ];

    private readonly SecretProtector _protector;

    public SecretProtectorTests()
    {
        _protector = new SecretProtector(ValidKeyBytes);
    }

    [Theory]
    [InlineData("ContraseñaSuperSecreta123!")]
    [InlineData("Server=myServerAddress;Database=myDataBase;Uid=myUsername;Pwd=myPassword;")]
    [InlineData("Texto con tildes áéíóú, ñ y símbolos: !@#$%^&*()_+-=[]{}|;':,.<>?/`~")]
    [InlineData("Query con caracteres Unicode y emojis: 🔍⚡🎯 SQL Server 2022")]
    [InlineData("")]
    public void EncryptAndDecryptReturnsOriginalPlainText(string originalText)
    {
        // Act
        string cipherText = _protector.Encrypt(originalText);
        string decryptedText = _protector.Decrypt(cipherText);

        // Assert
        Assert.NotEqual(originalText, cipherText);
        Assert.Equal(originalText, decryptedText);
    }

    [Fact]
    public void EncryptProducesDifferentCiphertextForSameInputDueToFreshNonce()
    {
        // Arrange
        const string input = "MismoTextoRepetido";

        // Act
        string cipher1 = _protector.Encrypt(input);
        string cipher2 = _protector.Encrypt(input);

        // Assert: Cifrados distintos por nonces independientes
        Assert.NotEqual(cipher1, cipher2);

        // Pero ambos descifran al mismo texto original
        Assert.Equal(input, _protector.Decrypt(cipher1));
        Assert.Equal(input, _protector.Decrypt(cipher2));
    }

    [Fact]
    public void DecryptWithWrongKeyThrowsCryptographicException()
    {
        // Arrange
        const string plainText = "CredencialDePrueba";
        string cipherText = _protector.Encrypt(plainText);

        using var otherProtector = new SecretProtector(AnotherKeyBytes);

        // Act & Assert
        Assert.Throws<CryptographicException>(() => otherProtector.Decrypt(cipherText));
    }

    [Fact]
    public void DecryptWithTamperedCiphertextThrowsCryptographicException()
    {
        // Arrange
        const string plainText = "TextoParaManipular";
        string cipherText = _protector.Encrypt(plainText);
        byte[] bytes = Convert.FromBase64String(cipherText);

        // Manipular el último byte del texto cifrado
        bytes[^1] ^= 0x01;
        string tamperedCipher = Convert.ToBase64String(bytes);

        // Act & Assert
        Assert.Throws<CryptographicException>(() => _protector.Decrypt(tamperedCipher));
    }

    [Fact]
    public void DecryptWithTamperedTagThrowsCryptographicException()
    {
        // Arrange
        const string plainText = "PruebaDeEtiqueta";
        string cipherText = _protector.Encrypt(plainText);
        byte[] bytes = Convert.FromBase64String(cipherText);

        // El tag está entre los bytes 13 y 28 (índice 13 en base 0)
        bytes[13] ^= 0x55;
        string tamperedCipher = Convert.ToBase64String(bytes);

        // Act & Assert
        Assert.Throws<CryptographicException>(() => _protector.Decrypt(tamperedCipher));
    }

    [Fact]
    public void DecryptWithTamperedVersionHeaderThrowsCryptographicException()
    {
        // Arrange
        string cipherText = _protector.Encrypt("ValidData");
        byte[] bytes = Convert.FromBase64String(cipherText);

        // Cambiar la versión 0x01 a 0x02
        bytes[0] = 0x02;
        string tamperedCipher = Convert.ToBase64String(bytes);

        // Act & Assert
        var ex = Assert.Throws<CryptographicException>(() => _protector.Decrypt(tamperedCipher));
        Assert.Contains("no compatible", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DecryptWithShortPayloadThrowsCryptographicException()
    {
        // Arrange: Menos de 29 bytes (encabezado mínimo)
        byte[] tooShort = [0x01, 0x02, 0x03];
        string shortBase64 = Convert.ToBase64String(tooShort);

        // Act & Assert
        Assert.Throws<CryptographicException>(() => _protector.Decrypt(shortBase64));
    }

    [Fact]
    public void DecryptWithInvalidBase64ThrowsCryptographicException()
    {
        // Act & Assert
        Assert.Throws<CryptographicException>(() => _protector.Decrypt("%%%NoEsBase64Valido%%%"));
    }

    [Theory]
    [InlineData(16)] // AES-128 (inválido para esta norma, se exige 256)
    [InlineData(24)] // AES-192
    [InlineData(48)] // Demasiado larga
    public void ConstructorWithInvalidKeyLengthThrowsArgumentException(int keyLength)
    {
        byte[] invalidKey = new byte[keyLength];
        Assert.Throws<ArgumentException>(() => new SecretProtector(invalidKey));
    }

    [Fact]
    public void ConstructorWithBase64KeyInitializesAndEncryptsCorrectly()
    {
        // Arrange
        string base64Key = Convert.ToBase64String(ValidKeyBytes);

        // Act
        using var protector = new SecretProtector(base64Key);
        string cipher = protector.Encrypt("TextoConClaveBase64");
        string decrypted = protector.Decrypt(cipher);

        // Assert
        Assert.Equal("TextoConClaveBase64", decrypted);
    }

    [Fact]
    public void ConstructorWithHexKeyInitializesAndEncryptsCorrectly()
    {
        // Arrange
        string hexKey = Convert.ToHexString(ValidKeyBytes);

        // Act
        using var protector = new SecretProtector(hexKey);
        string cipher = protector.Encrypt("TextoConClaveHex");
        string decrypted = protector.Decrypt(cipher);

        // Assert
        Assert.Equal("TextoConClaveHex", decrypted);
    }

    [Fact]
    public void ConstructorWithOptionsAndMasterKeyInitializesCorrectly()
    {
        // Arrange
        var options = Options.Create(new SecretProtectorOptions
        {
            MasterKey = Convert.ToBase64String(ValidKeyBytes)
        });

        // Act
        using var protector = new SecretProtector(options);
        string cipher = protector.Encrypt("TextoConOpciones");
        string decrypted = protector.Decrypt(cipher);

        // Assert
        Assert.Equal("TextoConOpciones", decrypted);
    }

    [Fact]
    public void ConstructorWithMasterKeyPathReadsKeyFromFile()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, Convert.ToBase64String(ValidKeyBytes));
            var options = Options.Create(new SecretProtectorOptions
            {
                MasterKeyPath = tempFile
            });

            // Act
            using var protector = new SecretProtector(options);
            string cipher = protector.Encrypt("TextoConClaveEnArchivo");
            string decrypted = protector.Decrypt(cipher);

            // Assert
            Assert.Equal("TextoConClaveEnArchivo", decrypted);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void ConstructorWithEnvironmentVariableResolvesKey()
    {
        // Arrange
        const string envVarName = "MASTER_ENCRYPTION_KEY";
        string originalValue = Environment.GetEnvironmentVariable(envVarName) ?? string.Empty;

        try
        {
            Environment.SetEnvironmentVariable(envVarName, Convert.ToBase64String(ValidKeyBytes));
            var options = Options.Create(new SecretProtectorOptions());

            // Act
            using var protector = new SecretProtector(options);
            string cipher = protector.Encrypt("TextoConClaveEnEnv");
            string decrypted = protector.Decrypt(cipher);

            // Assert
            Assert.Equal("TextoConClaveEnEnv", decrypted);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, string.IsNullOrEmpty(originalValue) ? null : originalValue);
        }
    }

    [Fact]
    public void ExceptionMessagesDoNotContainSensitivePlainText()
    {
        // Arrange
        const string secretPlainText = "UltraSecretPassword123!";
        string cipher = _protector.Encrypt(secretPlainText);

        using var otherProtector = new SecretProtector(AnotherKeyBytes);

        // Act
        var ex = Assert.Throws<CryptographicException>(() => otherProtector.Decrypt(cipher));

        // Assert: El mensaje no debe contener el texto sensible ni la clave
        Assert.DoesNotContain(secretPlainText, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(AnotherKeyBytes), ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _protector.Dispose();
        GC.SuppressFinalize(this);
    }
}
