using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.Google;

public static class GoogleServiceCollectionExtensions
{
    /// <summary>
    /// Registra a <see cref="global::Google.Apis.Http.IHttpClientFactory"/> compartilhada e chama
    /// <c>AddGoogleOAuth</c> (010d-1) e <c>AddGoogleApiClients</c> (010d-2). Fechado na 010-0: as trilhas só editam
    /// os arquivos dos dois métodos filhos.
    /// </summary>
    public static IServiceCollection AddGoogleIntegration(this IServiceCollection services)
    {
        services.AddSingleton<global::Google.Apis.Http.IHttpClientFactory, GoogleHttpClientFactory>();
        services.AddGoogleOAuth();
        services.AddGoogleApiClients();
        return services;
    }
}
