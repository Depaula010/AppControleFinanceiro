using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.Bot;

public static class BotServiceCollectionExtensions
{
    /// <summary>Registro do <c>IBotClient</c> (010b). Vazio na 010-0.</summary>
    public static IServiceCollection AddBotClient(this IServiceCollection services) => services;
}
