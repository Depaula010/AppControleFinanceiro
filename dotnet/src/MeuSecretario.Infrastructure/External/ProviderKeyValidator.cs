using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.External;

/// <summary>
/// Valida a chave de um usuário contra o provedor (RN-010-59; <c>api.py:2630-2672</c>). Gemini: 200. Weather (desvio
/// 019, WeatherAPI.com): diferente de 401. OpenRoute: diferente de 403, URL fixa. Timeout de 5 s no pipeline; rede ou
/// timeout viram <see cref="ProviderUnavailableException"/> sem a chave na mensagem.
/// </summary>
public sealed class ProviderKeyValidator(IHttpClientFactory httpClients, ILogger<ProviderKeyValidator> logger) : IProviderKeyValidator
{
    public async Task<KeyValidation> ValidateAsync(string provider, string key, CancellationToken cancellationToken)
    {
        using var request = provider switch
        {
            ApiProviders.Gemini => Build("https://generativelanguage.googleapis.com/v1beta/models", "x-goog-api-key", key),
            ApiProviders.Weather => Build($"{ExternalHttpOptions.WeatherBaseUrl}/current.json?key={Uri.EscapeDataString(key)}&q=London", null, null),
            ApiProviders.OpenRoute => Build("https://api.openrouteservice.org/v2/directions/driving-car", "Authorization", key),
            _ => null,
        };
        if (request is null)
        {
            return KeyValidation.NotSupported;
        }

        try
        {
            using var client = httpClients.CreateClient(ExternalHttpOptions.KeyValidationClient);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var status = response.StatusCode;
            var valid = provider switch
            {
                ApiProviders.Gemini => status == HttpStatusCode.OK,
                ApiProviders.Weather => status != HttpStatusCode.Unauthorized,
                _ => status != HttpStatusCode.Forbidden,
            };
            return valid ? KeyValidation.Valid : KeyValidation.Invalid;
        }
        catch (Exception ex) when (ExternalHttpOptions.IsTransient(ex, cancellationToken))
        {
            ExternalLog.ValidationUnavailable(logger, provider, ex.GetType().Name);
            throw new ProviderUnavailableException("Não foi possível validar (timeout ou erro de rede)");
        }
    }

    private static HttpRequestMessage Build(string url, string? header, string? value)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        return request;
    }
}
