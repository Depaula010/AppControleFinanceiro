using MeuSecretario.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace MeuSecretario.Infrastructure.Bot;

public static class BotServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <c>IBotClient</c> (010b): dois <c>HttpClient</c> nomeados (texto 10 s, imagem 30 s) com resiliência só
    /// de timeout (sem retry: reenviar um <c>send-message</c> aceito duplicaria a mensagem). O retry de 404 é de negócio
    /// e fica no <c>BotClient</c>. Nenhum header de chave em <c>DefaultRequestHeaders</c>.
    /// </summary>
    public static IServiceCollection AddBotClient(this IServiceCollection services)
    {
        services.AddHttpClient(BotHttpOptions.TextClient, client => client.Timeout = BotHttpOptions.TextTimeout)
            .AddResilienceHandler("bot-text-timeout", builder => builder.AddTimeout(BotHttpOptions.TextTimeout));
        services.AddHttpClient(BotHttpOptions.ImageClient, client => client.Timeout = BotHttpOptions.ImageTimeout)
            .AddResilienceHandler("bot-image-timeout", builder => builder.AddTimeout(BotHttpOptions.ImageTimeout));

        services.AddSingleton<BotSessionResolver>();
        services.AddSingleton<IBotClient, BotClient>();
        return services;
    }
}
