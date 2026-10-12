namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Contador diário ATÔMICO do limite de rotas (desvio 055). Existe porque <see cref="ICache"/> não tem incremento:
/// o valor é lido como <c>3</c> ou <c>"3"</c> e gravado como o texto JSON <c>"N"</c> (o que o Python grava), com TTL.
/// </summary>
public interface ITravelCounterStore
{
    /// <summary>Soma 1 numa operação só e renova o TTL. <c>null</c> se o Redis falhou ou está fora.</summary>
    Task<long?> IncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken);
}
