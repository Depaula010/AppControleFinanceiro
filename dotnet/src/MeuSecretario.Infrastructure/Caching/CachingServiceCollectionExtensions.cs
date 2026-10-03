using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.Caching;

public static class CachingServiceCollectionExtensions
{
    /// <summary>Registro do <c>ICache</c> (010a). Vazio na 010-0.</summary>
    public static IServiceCollection AddRedisCache(this IServiceCollection services) => services;
}
