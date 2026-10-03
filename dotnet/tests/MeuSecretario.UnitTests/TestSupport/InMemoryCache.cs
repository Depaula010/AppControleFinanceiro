using System.Text.Json;
using System.Text.RegularExpressions;
using MeuSecretario.Application.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.TestSupport;

/// <summary>
/// <see cref="ICache"/> em memória com o mesmo contrato fail-open do Redis: TTL pelo <see cref="FakeTimeProvider"/>,
/// glob simples (<c>*</c> e <c>?</c>), valor JSON ou cru como no Redis, e modo "indisponível".
/// </summary>
public sealed class InMemoryCache(FakeTimeProvider? time = null) : ICache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

    private readonly FakeTimeProvider _time = time ?? new FakeTimeProvider();
    private readonly Dictionary<string, (string Value, DateTimeOffset? Expires)> _store = [];
    private readonly object _gate = new();

    /// <summary>Quando <c>true</c> todas as operações devolvem o valor neutro, como com o Redis fora.</summary>
    public bool Unavailable { get; set; }

    /// <summary>TTL restante de uma chave (nulo se ausente ou sem TTL).</summary>
    public TimeSpan? TtlOf(string key)
    {
        lock (_gate)
        {
            return Live(key) is { Expires: { } expires } ? expires - _time.GetUtcNow() : null;
        }
    }

    /// <summary>Texto como está guardado (JSON ou cru).</summary>
    public string? RawValue(string key)
    {
        lock (_gate)
        {
            return Live(key)?.Value;
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(!Unavailable);

    public Task<bool> SetJsonAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        Task.FromResult(Put(key, JsonSerializer.Serialize(value, JsonOptions), ttl, onlyIfMissing: false));

    public Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Task.FromResult(default(T));
        }

        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<T>(raw, JsonOptions));
        }
        catch (JsonException)
        {
            return Task.FromResult(default(T));
        }
    }

    public Task<JsonElement?> GetJsonElementAsync(string key, CancellationToken cancellationToken = default)
    {
        var raw = Get(key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Task.FromResult<JsonElement?>(null);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var element = document.RootElement.Clone();
            return Task.FromResult<JsonElement?>(element.ValueKind == JsonValueKind.Null ? null : element);
        }
        catch (JsonException)
        {
            return Task.FromResult<JsonElement?>(null);
        }
    }

    public Task<bool> SetIfNotExistsAsync(string key, string rawValue, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        Task.FromResult(Put(key, rawValue, ttl, onlyIfMissing: true));

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Get(key) is not null);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (Unavailable)
        {
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            _store.Remove(key);
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<string>> KeysByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (Unavailable)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$", RegexOptions.CultureInvariant);
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            return Task.FromResult<IReadOnlyList<string>>(
                [.. _store.Where(p => (p.Value.Expires is null || p.Value.Expires > now) && regex.IsMatch(p.Key)).Select(p => p.Key)]);
        }
    }

    private bool Put(string key, string value, TimeSpan ttl, bool onlyIfMissing)
    {
        if (Unavailable)
        {
            return false;
        }

        lock (_gate)
        {
            if (onlyIfMissing && Live(key) is not null)
            {
                return false;
            }

            _store[key] = (value, _time.GetUtcNow() + ttl);
            return true;
        }
    }

    private string? Get(string key)
    {
        if (Unavailable)
        {
            return null;
        }

        lock (_gate)
        {
            return Live(key)?.Value;
        }
    }

    private (string Value, DateTimeOffset? Expires)? Live(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.Expires is { } expires && expires <= _time.GetUtcNow())
        {
            _store.Remove(key);
            return null;
        }

        return entry;
    }
}
