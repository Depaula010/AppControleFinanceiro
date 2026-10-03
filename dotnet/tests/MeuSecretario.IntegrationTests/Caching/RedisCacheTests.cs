using System.Diagnostics;
using System.Text.Json;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace MeuSecretario.IntegrationTests.Caching;

[Trait("Category", "Docker")]
public sealed class RedisCacheTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime, IDisposable
{
    private readonly CapturingLogger<RedisCache> _logger = new();
    private readonly FakeTimeProvider _time = new();
    private RedisCache _cache = default!;
    private ConnectionMultiplexer _raw = default!;
    private string _prefix = default!;

    public async Task InitializeAsync()
    {
        _cache = new RedisCache(RedisConnectionOptions.Parse(redis.RedisUrl), _time, _logger);
        _raw = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        _prefix = $"t{Guid.NewGuid():N}:";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _cache.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _raw.Dispose();
    }

    private IDatabase Db => _raw.GetDatabase();

    private string K(string name) => _prefix + name;

    [Fact]
    [Trait("AC", "010-01")]
    public async Task SetJson_WritesEquivalentJsonWithTtl()
    {
        (await _cache.SetJsonAsync(K("k"), new Dictionary<string, int> { ["a"] = 1 }, TimeSpan.FromSeconds(300))).ShouldBeTrue();

        ((string?)await Db.StringGetAsync(K("k"))).ShouldBe("{\"a\":1}");
        var ttl = await Db.KeyTimeToLiveAsync(K("k"));
        ttl.ShouldNotBeNull();
        ttl.Value.TotalSeconds.ShouldBeInRange(295, 300);
    }

    [Fact]
    [Trait("AC", "010-02")]
    public async Task GetJson_ReadsValuesExactlyAsPythonWroteThem()
    {
        await Db.StringSetAsync(K("s"), "\"abc\"");
        await Db.StringSetAsync(K("b"), "true");
        await Db.StringSetAsync(K("n"), "3");
        await Db.StringSetAsync(K("ns"), "\"3\"");
        await Db.StringSetAsync(K("o"), "{\"text\": \"a\\u00e7\\u00e3o\", \"x\": [1, 2]}");

        (await _cache.GetJsonAsync<string>(K("s"))).ShouldBe("abc");
        (await _cache.GetJsonAsync<bool>(K("b"))).ShouldBeTrue();
        (await _cache.GetJsonAsync<int>(K("n"))).ShouldBe(3);
        (await _cache.GetJsonAsync<int>(K("ns"))).ShouldBe(3);
        var element = await _cache.GetJsonElementAsync(K("o"));
        element.ShouldNotBeNull();
        element.Value.GetProperty("text").GetString().ShouldBe("ação");
        element.Value.GetProperty("x").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-03")]
    public async Task GetJson_AbsentEmptyRawAndNull_ReturnNull()
    {
        await Db.StringSetAsync(K("empty"), "");
        await Db.StringSetAsync(K("raw"), "running");
        await Db.StringSetAsync(K("null"), "null");

        (await _cache.GetJsonAsync<string>(K("absent"))).ShouldBeNull();
        (await _cache.GetJsonAsync<string>(K("empty"))).ShouldBeNull();
        (await _cache.GetJsonAsync<string>(K("raw"))).ShouldBeNull();
        (await _cache.GetJsonAsync<string>(K("null"))).ShouldBeNull();
        (await _cache.GetJsonElementAsync(K("raw"))).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-04")]
    public async Task SetIfNotExists_FirstWinsSecondLoses_RawValueAndTtl()
    {
        (await _cache.SetIfNotExistsAsync(K("lock"), "running", TimeSpan.FromSeconds(300))).ShouldBeTrue();
        (await _cache.SetIfNotExistsAsync(K("lock"), "other", TimeSpan.FromSeconds(300))).ShouldBeFalse();

        ((string?)await Db.StringGetAsync(K("lock"))).ShouldBe("running");
        (await Db.KeyTimeToLiveAsync(K("lock")))!.Value.TotalSeconds.ShouldBeInRange(295, 300);
    }

    [Fact]
    [Trait("AC", "010-04")]
    public async Task SetIfNotExists_FiftyConcurrentCalls_ExactlyOneWins()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => _cache.SetIfNotExistsAsync(K("race"), "x", TimeSpan.FromSeconds(60)))));

        results.Count(r => r).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-05")]
    public async Task ExistsAndDelete_ReflectTheKey_AndDeleteOfMissingKeyIsTrue()
    {
        await _cache.SetJsonAsync(K("e"), 1, TimeSpan.FromSeconds(60));

        (await _cache.ExistsAsync(K("e"))).ShouldBeTrue();
        (await _cache.DeleteAsync(K("e"))).ShouldBeTrue();
        (await _cache.ExistsAsync(K("e"))).ShouldBeFalse();
        (await _cache.DeleteAsync(K("never"))).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "010-06")]
    public async Task KeysByPattern_ReturnsOnlyMatches_AndEmptyWhenNone()
    {
        await _cache.SetJsonAsync(K("pending_event:5531:a"), 1, TimeSpan.FromSeconds(60));
        await _cache.SetJsonAsync(K("pending_event:5531:b"), 1, TimeSpan.FromSeconds(60));
        await _cache.SetJsonAsync(K("pending_event:5599:c"), 1, TimeSpan.FromSeconds(60));

        var keys = await _cache.KeysByPatternAsync(K("pending_event:5531:*"));

        keys.OrderBy(k => k, StringComparer.Ordinal).ToArray().ShouldBe([K("pending_event:5531:a"), K("pending_event:5531:b")]);
        (await _cache.KeysByPatternAsync(K("nothing:*"))).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-12")]
    public async Task SetJson_StringHoldingJson_IsDoubleEncodedAndReadBack()
    {
        await _cache.SetJsonAsync(K("sec"), "{\"a\":1}", TimeSpan.FromSeconds(60));

        // Lido como o json.loads do Python leria: o texto bruto é um JSON de texto cujo conteúdo é o JSON original.
        using var document = JsonDocument.Parse((string)(await Db.StringGetAsync(K("sec")))!);
        document.RootElement.GetString().ShouldBe("{\"a\":1}");
        (await _cache.GetJsonAsync<string>(K("sec"))).ShouldBe("{\"a\":1}");
    }

    [Fact]
    [Trait("AC", "010-13")]
    public async Task PythonFixtures_RoundTripThroughRedis()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "redis-bot-fixtures.json");
        using var fixtures = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var index = 0;
        foreach (var entry in fixtures.RootElement.GetProperty("redis_json").EnumerateArray())
        {
            var key = K($"fx:{index++}");
            var expected = JsonSerializer.Serialize(entry.GetProperty("value"));

            // Python grava, .NET lê.
            await Db.StringSetAsync(key, entry.GetProperty("json").GetString());
            var read = await _cache.GetJsonElementAsync(key);
            (read is null ? "null" : JsonSerializer.Serialize(read.Value)).ShouldBe(expected);

            // .NET grava, o parser JSON (equivalente ao json.loads) lê igual.
            await _cache.SetJsonAsync(key, entry.GetProperty("value"), TimeSpan.FromSeconds(60));
            using var written = JsonDocument.Parse((string)(await Db.StringGetAsync(key))!);
            JsonSerializer.Serialize(written.RootElement).ShouldBe(expected);
        }
    }

    [Fact]
    public async Task IsAvailable_TrueWithRedis()
    {
        (await _cache.IsAvailableAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "010-07")]
    public async Task Unavailable_AllOperationsReturnNeutralValuesQuickly_AndWarnOncePerMinute()
    {
        var options = RedisConnectionOptions.Parse("redis://127.0.0.1:1");
        options.ConnectTimeout = 300;
        await using var down = new RedisCache(options, _time, _logger);
        var watch = Stopwatch.StartNew();

        (await down.IsAvailableAsync()).ShouldBeFalse();
        (await down.SetJsonAsync("k", 1, TimeSpan.FromSeconds(5))).ShouldBeFalse();
        (await down.GetJsonAsync<string>("k")).ShouldBeNull();
        (await down.GetJsonElementAsync("k")).ShouldBeNull();
        (await down.SetIfNotExistsAsync("k", "v", TimeSpan.FromSeconds(5))).ShouldBeFalse();
        (await down.ExistsAsync("k")).ShouldBeFalse();
        (await down.DeleteAsync("k")).ShouldBeFalse();
        (await down.KeysByPatternAsync("*")).ShouldBeEmpty();

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(8));
        _logger.Warnings.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(61));
        (await down.ExistsAsync("k")).ShouldBeFalse();
        _logger.Warnings.ShouldBe(2);
        _logger.Messages.ShouldAllBe(m => !m.Contains("127.0.0.1", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "010-08")]
    public async Task RedisThatComesBack_IsUsedWithoutRestartingTheApplication()
    {
        var port = FreePort();
        await using var container = new RedisBuilder("redis:7-alpine").WithPortBinding(port, 6379).Build();
        var options = RedisConnectionOptions.Parse($"redis://127.0.0.1:{port}");
        options.ConnectTimeout = 300;
        await using var cache = new RedisCache(options, TimeProvider.System, _logger);

        (await cache.SetJsonAsync("k", 1, TimeSpan.FromSeconds(60))).ShouldBeFalse();

        await container.StartAsync();
        var worked = false;
        for (var attempt = 0; attempt < 60 && !worked; attempt++)
        {
            worked = await cache.SetJsonAsync("k", 1, TimeSpan.FromSeconds(60));
            if (!worked)
            {
                await Task.Delay(500);
            }
        }

        worked.ShouldBeTrue();
        (await cache.GetJsonAsync<int>("k")).ShouldBe(1);
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

/// <summary>Logger de teste que guarda as mensagens formatadas (para checar limite de avisos e ausência de dados).</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];
    private readonly object _gate = new();

    public int Warnings
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count(e => e.Level == LogLevel.Warning);
            }
        }
    }

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Select(e => e.Message)];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
