using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>Registro do <c>ILlmClient</c>, transporte e administração do cache (010c). Vazio na 010-0.</summary>
    public static IServiceCollection AddGeminiClient(this IServiceCollection services) => services;
}
