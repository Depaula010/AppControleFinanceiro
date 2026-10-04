using MeuSecretario.Application.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MeuSecretario.Infrastructure.External.Routes;

/// <summary>
/// Incremento atômico por script Lua (desvio 055). Lê <c>3</c> ou <c>"3"</c> (o que o Python gravou), soma 1 e grava
/// <c>"N"</c> com <c>SETEX</c>, o mesmo formato do <c>set_with_ttl</c> do Python. Conexão preguiçosa e fail-open: sem
/// Redis devolve <c>null</c>.
/// </summary>
public sealed class RedisTravelCounterStore(ConfigurationOptions options, ILogger<RedisTravelCounterStore> logger) : ITravelCounterStore, IAsyncDisposable
{
    private const string Script = """
        local current = redis.call('GET', KEYS[1])
        local n = 0
        if current then
          n = tonumber((string.gsub(current, '"', ''))) or 0
        end
        n = math.floor(n) + 1
        redis.call('SETEX', KEYS[1], ARGV[1], '"' .. string.format('%d', n) .. '"')
        return n
        """;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private ConnectionMultiplexer? _multiplexer;

    public async Task<long?> IncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        try
        {
            var multiplexer = await GetAsync(cancellationToken).ConfigureAwait(false);
            if (!multiplexer.IsConnected)
            {
                return null;
            }

            var result = await multiplexer.GetDatabase()
                .ScriptEvaluateAsync(Script, [(RedisKey)key], [(RedisValue)(long)ttl.TotalSeconds])
                .WaitAsync(cancellationToken).ConfigureAwait(false);
            return (long)result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ExternalLog.RateLimitFailed(logger, "increment", ex.GetType().Name);
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.CloseAsync().ConfigureAwait(false);
            _multiplexer.Dispose();
        }

        _gate.Dispose();
    }

    private async Task<ConnectionMultiplexer> GetAsync(CancellationToken cancellationToken)
    {
        if (_multiplexer is not null)
        {
            return _multiplexer;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _multiplexer ??= await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
