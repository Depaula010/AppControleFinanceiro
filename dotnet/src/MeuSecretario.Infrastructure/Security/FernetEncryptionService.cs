using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Infrastructure.Security;

/// <summary>
/// Fernet (https://github.com/fernet/spec). Chave: base64 url-safe de 32 bytes (16 de assinatura + 16 de AES).
/// Token: <c>0x80 | timestamp(8, big-endian) | IV(16) | AES-128-CBC/PKCS7 | HMAC-SHA256(32)</c>, em base64 url-safe com padding.
/// </summary>
public sealed class FernetEncryptionService : IEncryptionService
{
    private const byte Version = 0x80;
    private const int HmacLength = 32;
    private const int IvLength = 16;
    private const int HeaderLength = 1 + 8 + IvLength;
    private const int MinTokenLength = HeaderLength + 16 + HmacLength;
    private const string FernetPrefix = "gAAAAA";
    private const int LooksEncryptedMinLength = 40;

    private readonly byte[] _signingKey;
    private readonly byte[] _encryptionKey;
    private readonly TimeProvider _timeProvider;

    public FernetEncryptionService(string key, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;

        byte[] raw;
        try
        {
            raw = FromUrlSafeBase64(key);
        }
        catch (FormatException)
        {
            // Sem o valor da chave na mensagem.
            throw new ArgumentException("ENCRYPTION_KEY inválida: deve ser uma chave Fernet (base64 url-safe de 32 bytes).", nameof(key));
        }

        if (raw.Length != 32)
        {
            throw new ArgumentException("ENCRYPTION_KEY inválida: deve ser uma chave Fernet (base64 url-safe de 32 bytes).", nameof(key));
        }

        _signingKey = raw[..16];
        _encryptionKey = raw[16..];
    }

    /// <summary>Se a chave é base64 url-safe de 32 bytes (valida o <c>ENCRYPTION_KEY</c> no boot).</summary>
    public static bool IsValidKey(string key)
    {
        try
        {
            return FromUrlSafeBase64(key).Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public string Encrypt(string plaintext)
    {
        var iv = RandomNumberGenerator.GetBytes(IvLength);
        return EncryptFromParts(plaintext, _timeProvider.GetUtcNow().ToUnixTimeSeconds(), iv);
    }

    /// <summary>Cifra com timestamp e IV fixos (somente testes de equivalência com o Python).</summary>
    internal string EncryptFromParts(string plaintext, long timestamp, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        var ciphertext = aes.EncryptCbc(Encoding.UTF8.GetBytes(plaintext), iv, PaddingMode.PKCS7);

        var token = new byte[HeaderLength + ciphertext.Length + HmacLength];
        token[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(1, 8), timestamp);
        iv.CopyTo(token.AsSpan(9, IvLength));
        ciphertext.CopyTo(token.AsSpan(HeaderLength));

        var hmac = HMACSHA256.HashData(_signingKey, token.AsSpan(0, HeaderLength + ciphertext.Length));
        hmac.CopyTo(token.AsSpan(HeaderLength + ciphertext.Length));
        return ToUrlSafeBase64(token);
    }

    public string Decrypt(string token)
    {
        byte[] data;
        try
        {
            data = FromUrlSafeBase64(token);
        }
        catch (FormatException)
        {
            throw new CryptographicException("Token Fernet inválido.");
        }

        if (data.Length < MinTokenLength || data[0] != Version)
        {
            throw new CryptographicException("Token Fernet inválido.");
        }

        var signedLength = data.Length - HmacLength;
        var expected = HMACSHA256.HashData(_signingKey, data.AsSpan(0, signedLength));
        if (!CryptographicOperations.FixedTimeEquals(expected, data.AsSpan(signedLength)))
        {
            throw new CryptographicException("Token Fernet inválido.");
        }

        try
        {
            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            var plain = aes.DecryptCbc(data.AsSpan(HeaderLength, signedLength - HeaderLength), data.AsSpan(9, IvLength), PaddingMode.PKCS7);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Token Fernet inválido.");
        }
    }

    public bool LooksEncrypted(string value) =>
        value.Length >= LooksEncryptedMinLength && value.StartsWith(FernetPrefix, StringComparison.Ordinal);

    public string DecryptOrPlain(string value)
    {
        if (!LooksEncrypted(value))
        {
            return value;
        }

        try
        {
            return Decrypt(value);
        }
        catch (CryptographicException)
        {
            return value;
        }
    }

    private static byte[] FromUrlSafeBase64(string value)
    {
        var text = value.Trim().Replace('-', '+').Replace('_', '/');
        text = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        return Convert.FromBase64String(text);
    }

    private static string ToUrlSafeBase64(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_');
}
