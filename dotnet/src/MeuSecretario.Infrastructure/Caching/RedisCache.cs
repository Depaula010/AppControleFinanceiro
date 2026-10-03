using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MeuSecretario.Infrastructure.Caching;

/// <summary>
/// <see cref="ICache"/> sobre o StackExchange.Redis (spec 010 §4). Fail-open: sem Redis ou em erro devolve o valor
/// neutro e nunca lança; o aviso sai no máximo 1 vez por minuto. Conecta de forma preguiçosa (o app sobe com o Redis
/// fora) e o multiplexador se reconecta sozinho quando o Redis volta.
/// </summary>
public sealed class RedisCache(ConfigurationOptions options, TimeProvider time, ILogger<RedisCache> logger) : ICache, IAsyncDisposable
{
    private static readonly TimeSpan WarnInterval = TimeSpan.FromMinutes(1);
    private const int ScanPageSize = 250;

    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private ConnectionMultiplexer? _multiplexer;
    private long _lastWarnTimestamp;
    private int _warned;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("ping", async (_, db) =>
        {
            await db.PingAsync();
            return true;
        }, false, cancellationToken);

    public Task<bool> SetJsonAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        ExecuteAsync("set", (_, db) => db.StringSetAsync(key, CacheJson.Serialize(value), ttl), false, cancellationToken);

    public async Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) =>
        CacheJson.Deserialize<T>(await GetRawAsync(key, cancellationToken));

    public async Task<JsonElement?> GetJsonElementAsync(string key, CancellationToken cancellationToken = default) =>
        CacheJson.ParseElement(await GetRawAsync(key, cancellationToken));

    public Task<bool> SetIfNotExistsAsync(string key, string rawValue, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        ExecuteAsync("set_nx", (_, db) => db.StringSetAsync(key, rawValue, ttl, When.NotExists), false, cancellationToken);

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        ExecuteAsync("exists", (_, db) => db.KeyExistsAsync(key), false, cancellationToken);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        ExecuteAsync("delete", async (_, db) =>
        {
            await db.KeyDeleteAsync(key);
            return true;
        }, false, cancellationToken);

    public Task<IReadOnlyList<string>> KeysByPatternAsync(string pattern, CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyList<string>>("keys", async (multiplexer, db) =>
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);
                if (server.IsReplica || !server.IsConnected)
                {
                    continue;
                }

                await foreach (var key in server.KeysAsync(db.Database, pattern, ScanPageSize).WithCancellation(cancellationToken))
                {
                    keys.Add(key.ToString());
                }
            }

            return [.. keys];
        }, [], cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.CloseAsync();
            _multiplexer.Dispose();
        }

        _connectGate.Dispose();
    }

    private async Task<string?> GetRawAsync(string key, CancellationToken cancellationToken)
    {
        var value = await ExecuteAsync("get", (_, db) => db.StringGetAsync(key), RedisValue.Null, cancellationToken);
        return value.IsNull ? null : (string?)value;
    }

    private async Task<T> ExecuteAsync<T>(string operation, Func<IConnectionMultiplexer, IDatabase, Task<T>> action, T neutral, CancellationToken cancellationToken)
    {
        try
        {
            var multiplexer = await GetMultiplexerAsync(cancellationToken);
            if (multiplexer is null || !multiplexer.IsConnected)
            {
                Warn(() => CacheLog.Unavailable(logger, operation));
                return neutral;
            }

            return await action(multiplexer, multiplexer.GetDatabase()).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Warn(() => CacheLog.OperationFailed(logger, operation, ex.GetType().Name));
            return neutral;
        }
    }

    private async Task<ConnectionMultiplexer?> GetMultiplexerAsync(CancellationToken cancellationToken)
    {
        if (_multiplexer is not null)
        {
            return _multiplexer;
        }

        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            // Com AbortOnConnectFail = false o ConnectAsync devolve um multiplexador que reconecta em segundo plano.
            _multiplexer ??= await ConnectionMultiplexer.ConnectAsync(options);
            return _multiplexer;
        }
        finally
        {
            _connectGate.Release();
        }
    }

    /// <summary>Executa o log só se passou 1 minuto desde o último aviso (RN-010-06).</summary>
    private void Warn(Action log)
    {
        var now = time.GetTimestamp();
        var last = Interlocked.Read(ref _lastWarnTimestamp);
        var first = Volatile.Read(ref _warned) == 0;
        if (!first && time.GetElapsedTime(last, now) < WarnInterval)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _lastWarnTimestamp, now, last) != last)
        {
            return;
        }

        Volatile.Write(ref _warned, 1);
        log();
    }
}
