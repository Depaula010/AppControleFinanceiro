using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <c>ICache</c> (singleton) sobre <c>REDIS_URL</c>. Não conecta no boot: a conexão é preguiçosa.
    /// Também registra o limpador real do cache do usuário, que prevalece sobre o nulo da 008 (última registração vence).
    /// </summary>
    public static IServiceCollection AddRedisCache(this IServiceCollection services)
    {
        services.AddSingleton<ICache>(sp => new RedisCache(
            RedisConnectionOptions.Parse(sp.GetRequiredService<AppSettings>().RedisUrl),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RedisCache>>()));
        services.AddSingleton<IUserCacheCleaner, UserCacheCleaner>();
        return services;
    }
}
