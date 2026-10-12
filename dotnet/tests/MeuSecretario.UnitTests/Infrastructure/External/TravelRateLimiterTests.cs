using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.External.Routes;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.UnitTests.Infrastructure.Bot;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Infrastructure.External;

public class TravelRateLimiterTests
{
    private sealed class FakeStore : ITravelCounterStore
    {
        public List<(string Key, TimeSpan Ttl)> Calls { get; } = [];

        public long? Result { get; set; } = 1;

        public bool Throw { get; set; }

        public Task<long?> IncrementAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
        {
            Calls.Add((key, ttl));
            return Throw ? throw new InvalidOperationException("boom") : Task.FromResult(Result);
        }
    }

    // 2026-10-03 23:30 em São Paulo (UTC-3) = 2026-10-04 02:30 UTC.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 4, 2, 30, 0, TimeSpan.Zero));
    private readonly InMemoryCache _cache = new();
    private readonly FakeStore _store = new();
    private readonly FakeIntegrationSettings _settings = new() { TravelTimeDailyLimit = 10 };
    private readonly ListLogger<TravelRateLimiter> _log = new();

    private const string Key = "travel_time_limit:7:2026-10-03";

    private TravelRateLimiter Limiter() => new(_cache, _store, new BrazilClock(_time), _settings, _log);

    private Task<bool> Seed(string raw) => _cache.SetIfNotExistsAsync(Key, raw, TimeSpan.FromHours(1));

    [Theory]
    [InlineData("9", true, 1, 9)]
    [InlineData("10", false, 0, 10)]
    [InlineData("11", false, -1, 11)]
    [InlineData("\"3\"", true, 7, 3)]
    [InlineData("3", true, 7, 3)]
    [Trait("AC", "010-120")]
    public async Task Check_ReadsNumberAndQuotedTextAndAppliesTheLimit(string raw, bool allowed, int remaining, int used)
    {
        await Seed(raw);

        (await Limiter().CheckAsync(7, default)).ShouldBe(new TravelRateStatus(allowed, remaining, used));
    }

    [Fact]
    [Trait("AC", "010-120")]
    public async Task Check_MissingKeyOrRedisDown_IsUsedZeroAndAllowed()
    {
        (await Limiter().CheckAsync(7, default)).ShouldBe(new TravelRateStatus(true, 10, 0));

        await Seed("5");
        _cache.Unavailable = true;
        (await Limiter().CheckAsync(7, default)).ShouldBe(new TravelRateStatus(true, 10, 0));
    }

    [Fact]
    [Trait("AC", "010-120")]
    public async Task Check_InternalError_IsAllowedWithFixedTen_NotTheConfiguredLimit()
    {
        _settings.TravelTimeDailyLimit = 3;
        await Seed("\"abc\"");

        (await Limiter().CheckAsync(7, default)).ShouldBe(new TravelRateStatus(true, 10, 0));
    }

    [Fact]
    [Trait("AC", "010-121")]
    public async Task Increment_UsesTheSaoPauloDate_AndTheOneDayTtl()
    {
        (await Limiter().IncrementAsync(7, default)).ShouldBeTrue();

        _store.Calls.ShouldBe([(Key, TimeSpan.FromSeconds(86400))]);
    }

    [Fact]
    [Trait("AC", "010-121")]
    public async Task Increment_RedisDownOrError_ReturnsFalse()
    {
        _store.Result = null;
        (await Limiter().IncrementAsync(7, default)).ShouldBeFalse();

        _store.Throw = true;
        (await Limiter().IncrementAsync(7, default)).ShouldBeFalse();
    }
}
