namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Decide a chave de API de uma chamada ao Gemini, WeatherAPI ou OpenRouteService (RN-010-66).
/// <paramref name="userId"/> nulo usa a chave do sistema sem consultar o 008.
/// </summary>
public interface IProviderKeySelector
{
    /// <exception cref="ArgumentException">Provedor desconhecido.</exception>
    Task<ProviderKeyResult> SelectAsync(int? userId, string provider, CancellationToken cancellationToken);
}

public sealed record ApiKeyFailureInfo(ApiKeyFailure Reason, string Message);

/// <summary>
/// Chave escolhida ou falha. <c>Key</c> e <c>Failure</c> nulos juntos = chave do sistema vazia ("sem chave").
/// </summary>
public sealed record ProviderKeyResult(string? Key, ApiKeyFailureInfo? Failure)
{
    public bool HasKey => !string.IsNullOrEmpty(Key);

    public static ProviderKeyResult WithKey(string key) => new(key, null);

    public static ProviderKeyResult Failed(ApiKeyFailure reason, string message) => new(null, new ApiKeyFailureInfo(reason, message));

    public static ProviderKeyResult NoKey { get; } = new(null, null);

    /// <summary>Nunca inclui a chave.</summary>
    public override string ToString() => Failure is null
        ? $"ProviderKeyResult {{ Key = {(HasKey ? "***" : "(none)")} }}"
        : $"ProviderKeyResult {{ Failure = {Failure.Reason} }}";
}
