using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.External.Routes;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Caching;

/// <summary>AC-010-120/121 contra o Redis real: compatibilidade com o formato do Python e incremento atômico.</summary>
[Trait("Category", "Docker")]
public sealed class TravelRateLimiterRedisTests(RedisFixture redis) : IClassFixture<RedisFixture>, IDisposable
{
    private sealed class FixedSettings : IIntegrationSettings
    {
        public string GeminiApiKey => string.Empty;

        public string GeminiModel => string.Empty;

        public string ApiSecretKey => string.Empty;

        public string BotWhatsappUrl => string.Empty;

        public string BotApiKey => string.Empty;

        public string BotSessionName => string.Empty;

        public string BotSessionId => string.Empty;

        public string GoogleClientId => string.Empty;

        public string GoogleClientSecret => string.Empty;

        public string GoogleRedirectUri => string.Empty;

        public string WeatherApiKey => string.Empty;

        public string OpenRouteApiKey => string.Empty;

        public string OpenRouteBaseUrl => string.Empty;

        public int TravelTimeDailyLimit => 10;
        public DateTimeOffset? GoogleConnectLegacyUntil => null;
    }

    private readonly ConnectionMultiplexer _mux = ConnectionMultiplexer.Connect(redis.ConnectionString);
    private readonly RedisCache _cache = new(RedisConnectionOptions.Parse(redis.RedisUrl), TimeProvider.System, NullLogger<RedisCache>.Instance);
    private readonly RedisTravelCounterStore _store = new(RedisConnectionOptions.Parse(redis.RedisUrl), NullLogger<RedisTravelCounterStore>.Instance);
    private readonly int _userId = Random.Shared.Next(100000, 999999);

    public void Dispose()
    {
        _store.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _cache.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _mux.Dispose();
    }

    private TravelRateLimiter Limiter() => new(_cache, _store, new BrazilClock(TimeProvider.System), new FixedSettings(), NullLogger<TravelRateLimiter>.Instance);

    private string Key() => $"travel_time_limit:{_userId}:{new BrazilClock(TimeProvider.System).Today:yyyy-MM-dd}";

    [Theory]
    [InlineData("3")]
    [InlineData("\"3\"")]
    public async Task Check_ReadsWhatThePythonWrote_AsNumberOrQuotedText(string raw)
    {
        await _mux.GetDatabase().StringSetAsync(Key(), raw);

        (await Limiter().CheckAsync(_userId, default)).ShouldBe(new TravelRateStatus(true, 7, 3));
    }

    [Theory]
    [InlineData("3")]
    [InlineData("\"3\"")]
    public async Task Increment_ContinuesFromEitherFormat_AndWritesQuotedTextWithTtl(string raw)
    {
        await _mux.GetDatabase().StringSetAsync(Key(), raw);

        (await Limiter().IncrementAsync(_userId, default)).ShouldBeTrue();

        var stored = (string?)await _mux.GetDatabase().StringGetAsync(Key());
        stored.ShouldBe("\"4\"");
        var ttl = (await _mux.GetDatabase().KeyTimeToLiveAsync(Key()))!.Value;
        ttl.ShouldBeGreaterThan(TimeSpan.FromSeconds(86300));
        ttl.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(86400));
        (await Limiter().CheckAsync(_userId, default)).Used.ShouldBe(4);
    }

    [Fact]
    public async Task Increment_FromMissingKey_StartsAtOne()
    {
        (await Limiter().IncrementAsync(_userId, default)).ShouldBeTrue();

        ((string?)await _mux.GetDatabase().StringGetAsync(Key())).ShouldBe("\"1\"");
    }

    [Fact]
    public async Task Increment_FiftyConcurrentCalls_LoseNoUpdate()
    {
        var limiter = Limiter();

        var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => limiter.IncrementAsync(_userId, default)));

        results.ShouldAllBe(r => r);
        ((string?)await _mux.GetDatabase().StringGetAsync(Key())).ShouldBe("\"50\"");
    }
}
