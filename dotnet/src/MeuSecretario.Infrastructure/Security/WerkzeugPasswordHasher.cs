using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MeuSecretario.Application.Abstractions;
using Org.BouncyCastle.Crypto.Generators;

namespace MeuSecretario.Infrastructure.Security;

/// <summary>
/// Reproduz <c>werkzeug.security.generate_password_hash/check_password_hash</c> (Werkzeug 3.x).
/// <list type="bullet">
/// <item><c>pbkdf2:sha256:N$salt$hex</c>: PBKDF2-HMAC-SHA256, 32 bytes, hex minúsculo; as iterações vêm do próprio hash.</item>
/// <item><c>scrypt:N:r:p$salt$hex</c>: scrypt, 64 bytes, hex minúsculo.</item>
/// </list>
/// O salt são caracteres ASCII usados como bytes UTF-8 (não é base64).
/// </summary>
public sealed class WerkzeugPasswordHasher : IPasswordHasher
{
    public const int MinIterations = 1000;
    public const int DefaultIterations = 1_000_000; // padrão do Werkzeug 3.1
    public const int MaxIterationsAllowed = 10_000_000; // limite anti-DoS (verificação e configuração)

    private const int SaltLength = 16;
    private const string SaltChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int MaxIterations = MaxIterationsAllowed;
    private const long MaxScryptMemoryBytes = 134_217_728; // 128 MiB (o Werkzeug usa maxmem de 132 MB)
    private const int ScryptKeyLength = 64;
    private const int Pbkdf2KeyLength = 32;

    private readonly int _iterations;

    public WerkzeugPasswordHasher(int iterations = DefaultIterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, MinIterations);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(iterations, MaxIterations);
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetString(SaltChars, SaltLength);
        return Pbkdf2Hash(password, salt, _iterations);
    }

    /// <summary>Hash <c>pbkdf2:sha256</c> determinístico para um salt dado (usado pelo teste de equivalência bit a bit).</summary>
    public static string Pbkdf2Hash(string password, string salt, int iterations)
    {
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), iterations, HashAlgorithmName.SHA256, Pbkdf2KeyLength);
        return $"pbkdf2:sha256:{iterations.ToString(CultureInfo.InvariantCulture)}${salt}${Convert.ToHexStringLower(derived)}";
    }

    public bool Verify(string? hash, string password)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var parts = hash.Split('$');
        if (parts.Length != 3 || parts[1].Length == 0 || parts[2].Length == 0)
        {
            return false;
        }

        var method = parts[0].Split(':');
        string? expected = method[0] switch
        {
            "pbkdf2" => ComputePbkdf2(method, password, parts[1]),
            "scrypt" => ComputeScrypt(method, password, parts[1]),
            _ => null,
        };

        return expected is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2]));
    }

    private static string? ComputePbkdf2(string[] method, string password, string salt)
    {
        // "pbkdf2:sha256" (iterações omitidas = padrão do Werkzeug) ou "pbkdf2:sha256:N"
        if (method.Length is < 2 or > 3 || method[1] != "sha256")
        {
            return null;
        }

        var iterations = DefaultIterations;
        if (method.Length == 3
            && (!int.TryParse(method[2], NumberStyles.None, CultureInfo.InvariantCulture, out iterations)
                || iterations is < 1 or > MaxIterations))
        {
            return null;
        }

        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), iterations, HashAlgorithmName.SHA256, Pbkdf2KeyLength);
        return Convert.ToHexStringLower(derived);
    }

    private static string? ComputeScrypt(string[] method, string password, string salt)
    {
        if (method.Length != 4
            || !int.TryParse(method[1], NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            || !int.TryParse(method[2], NumberStyles.None, CultureInfo.InvariantCulture, out var r)
            || !int.TryParse(method[3], NumberStyles.None, CultureInfo.InvariantCulture, out var p))
        {
            return null;
        }

        // N potência de 2 maior que 1; r e p positivos; memória (128·N·r) limitada.
        if (n < 2 || (n & (n - 1)) != 0 || r < 1 || p < 1 || 128L * n * r > MaxScryptMemoryBytes)
        {
            return null;
        }

        var derived = SCrypt.Generate(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), n, r, p, ScryptKeyLength);
        return Convert.ToHexStringLower(derived);
    }
}
