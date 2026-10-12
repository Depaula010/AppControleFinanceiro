using System.Security.Cryptography;
using System.Text;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Infrastructure.Security;

/// <summary>
/// Compara o <c>X-API-KEY</c> com a <c>API_SECRET_KEY</c> em tempo constante: SHA-256 dos dois lados
/// (tamanho fixo, sem vazar o comprimento) e <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// O Flask usa <c>!=</c>.
/// </summary>
public sealed class ApiSecretKeyValidator : IApiKeyValidator
{
    private readonly byte[] _secretHash;

    public ApiSecretKeyValidator(string secret)
    {
        _secretHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret ?? string.Empty));
        HasSecret = !string.IsNullOrEmpty(secret);
    }

    private bool HasSecret { get; }

    public bool IsValid(string? presented)
    {
        if (!HasSecret || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        return CryptographicOperations.FixedTimeEquals(presentedHash, _secretHash);
    }
}
