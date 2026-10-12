using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.Infrastructure.External.Routes;
using MeuSecretario.Infrastructure.External.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace MeuSecretario.Infrastructure.External;

public static class ExternalServiceCollectionExtensions
{
    /// <summary>
    /// Registra clima, rotas, validador de chaves e limitador (010e). <c>HttpClient</c> "weather" e "key-validation":
    /// 5 s; "openroute": 15 s por tentativa. O retry do OpenRoute (2 tentativas em timeout, rede e 5xx, desvio 064)
    /// fica no <c>OpenRouteClient</c>, que precisa distinguir 4xx de 5xx e reconstruir a requisição com a chave do
    /// usuário; o pipeline só impõe o timeout. Nenhum header de chave em <c>DefaultRequestHeaders</c>.
    /// </summary>
    public static IServiceCollection AddWeatherAndRoutes(this IServiceCollection services)
    {
        AddTimeoutClient(services, ExternalHttpOptions.WeatherClient, ExternalHttpOptions.WeatherTimeout);
        AddTimeoutClient(services, ExternalHttpOptions.KeyValidationClient, ExternalHttpOptions.ValidationTimeout);
        AddTimeoutClient(services, ExternalHttpOptions.RouteClient, ExternalHttpOptions.RouteTimeout);

        services.AddSingleton<IWeatherClient, WeatherApiClient>();
        services.AddSingleton<IRouteClient, OpenRouteClient>();
        services.AddSingleton<IProviderKeyValidator, ProviderKeyValidator>();
        services.AddSingleton<ITravelCounterStore>(sp => new RedisTravelCounterStore(
            RedisConnectionOptions.Parse(sp.GetRequiredService<AppSettings>().RedisUrl),
            sp.GetRequiredService<ILogger<RedisTravelCounterStore>>()));
        services.AddSingleton<ITravelRateLimiter, TravelRateLimiter>();
        return services;
    }

    private static void AddTimeoutClient(IServiceCollection services, string name, TimeSpan timeout) =>
        services.AddHttpClient(name, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler(name, builder => builder.AddTimeout(timeout));
}
