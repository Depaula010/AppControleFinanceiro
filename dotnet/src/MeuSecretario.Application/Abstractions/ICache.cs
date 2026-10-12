using System.Text.Json;

namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Cache e locks no Redis (equivale a <c>RedisService</c>). Todas as operações são fail-open: sem Redis ou em erro
/// devolvem o valor neutro e nunca lançam (spec 010 §4.1). Quem precisa distinguir "indisponível" de "falso" usa
/// <see cref="IsAvailableAsync"/>.
/// </summary>
public interface ICache
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary><c>SETEX</c> do JSON do valor (texto simples vira JSON de texto, com aspas). <c>true</c> se gravou.</summary>
    Task<bool> SetJsonAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Ausente, vazio, JSON inválido ou JSON <c>null</c> devolvem <c>null</c>.</summary>
    Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default);

    Task<JsonElement?> GetJsonElementAsync(string key, CancellationToken cancellationToken = default);

    /// <summary><c>SET key rawValue NX EX ttl</c> com valor cru. <c>true</c> só se o Redis confirmou; <c>false</c> se já existia ou falhou.</summary>
    Task<bool> SetIfNotExistsAsync(string key, string rawValue, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary><c>true</c> se não houve erro, mesmo que a chave não existisse.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Glob do Redis (<c>SCAN</c>); ordem não garantida; vazia sem Redis.</summary>
    Task<IReadOnlyList<string>> KeysByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}
