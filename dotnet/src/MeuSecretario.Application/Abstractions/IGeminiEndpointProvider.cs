namespace MeuSecretario.Application.Abstractions;

/// <summary>Modelo e versão da API do Gemini em vigor (desvio 263).</summary>
public sealed record GeminiEndpoint(string Model, string ApiVersion);

/// <summary>Resolve o modelo e a versão da API do Gemini a cada chamada (tabela <c>configuracoes</c>, com cache curto).</summary>
public interface IGeminiEndpointProvider
{
    Task<GeminiEndpoint> GetAsync(CancellationToken cancellationToken);
}
