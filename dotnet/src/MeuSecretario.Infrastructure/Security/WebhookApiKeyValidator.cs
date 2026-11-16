using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Configuration;

namespace MeuSecretario.Infrastructure.Security;

/// <summary>
/// O <c>x-api-key</c> de <c>/webhook-whatsapp</c> (desvio 284): <c>WEBHOOK_API_KEY</c> quando existe, senão a <c>API_SECRET_KEY</c>. É um tipo
/// próprio (e não o <see cref="IApiKeyValidator"/> geral) para o <c>/admin</c> e as demais rotas continuarem exigindo só a <c>API_SECRET_KEY</c>.
/// </summary>
public sealed class WebhookApiKeyValidator(AppSettings settings) : IApiKeyValidator
{
    private readonly ApiSecretKeyValidator _inner =
        new(string.IsNullOrEmpty(settings.WebhookApiKey) ? settings.ApiSecretKey : settings.WebhookApiKey);

    public bool IsValid(string? presented) => _inner.IsValid(presented);
}
