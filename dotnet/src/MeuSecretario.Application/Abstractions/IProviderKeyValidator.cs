namespace MeuSecretario.Application.Abstractions;

/// <summary>Valida uma chave de API contra o provedor (spec 010 RN-010-59; usada pela 007).</summary>
public interface IProviderKeyValidator
{
    /// <exception cref="ProviderUnavailableException">Timeout ou erro de rede.</exception>
    Task<KeyValidation> ValidateAsync(string provider, string key, CancellationToken cancellationToken);
}

public enum KeyValidation
{
    Valid,
    Invalid,
    NotSupported,
}

/// <summary>Provedor inacessível na validação (a 007 responde 503).</summary>
public sealed class ProviderUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
