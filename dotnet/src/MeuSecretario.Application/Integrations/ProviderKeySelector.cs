using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Application.Integrations;

/// <summary>RN-010-66: chave do sistema sem usuário; com usuário, a resolução do 008.</summary>
public sealed class ProviderKeySelector(IApiKeyProvider apiKeys, IIntegrationSettings settings) : IProviderKeySelector
{
    public async Task<ProviderKeyResult> SelectAsync(int? userId, string provider, CancellationToken cancellationToken)
    {
        if (!ApiProviders.IsValid(provider))
        {
            throw new ArgumentException("Provedor desconhecido.", nameof(provider));
        }

        if (userId is not { } id)
        {
            var systemKey = SystemKey(provider);
            return string.IsNullOrEmpty(systemKey) ? ProviderKeyResult.NoKey : ProviderKeyResult.WithKey(systemKey);
        }

        return await apiKeys.ResolveAsync(id, provider, cancellationToken).ConfigureAwait(false) switch
        {
            ApiKeyResolution.Resolved resolved => ProviderKeyResult.WithKey(resolved.Key),
            ApiKeyResolution.Failed failed => ProviderKeyResult.Failed(failed.Reason, failed.Message),
            _ => throw new InvalidOperationException("Resolução de chave desconhecida."),
        };
    }

    private string SystemKey(string provider) => provider switch
    {
        ApiProviders.Gemini => settings.GeminiApiKey,
        ApiProviders.Weather => settings.WeatherApiKey,
        _ => settings.OpenRouteApiKey,
    };
}
