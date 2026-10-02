using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.External;

public static class ExternalServiceCollectionExtensions
{
    /// <summary>Registro de clima, rotas, validador de chaves e limitador (010e). Vazio na 010-0.</summary>
    public static IServiceCollection AddWeatherAndRoutes(this IServiceCollection services) => services;
}
