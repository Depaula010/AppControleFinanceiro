using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Security;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.IntegrationTests.Caching;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Security;

/// <summary>Valores <c>security:*</c> no Redis real: codificação dupla, TTLs, leitura do que o Python gravou, SCAN (T3).</summary>
[Trait("Category", "Docker")]
public sealed class RedisSecurityStoreTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime, IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 4, 10, 0, 0, 123, DateTimeKind.Unspecified);

    private readonly string _ip = $"t-{Guid.NewGuid():N}";
    private RedisCache _cache = default!;
    private ConnectionMultiplexer _raw = default!;
    private RedisSecurityStore _store = default!;

    public async Task InitializeAsync()
    {
        _cache = new RedisCache(RedisConnectionOptions.Parse(redis.RedisUrl), new FakeTimeProvider(), new CapturingLogger<RedisCache>());
        _raw = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString + ",allowAdmin=true");
        _store = new RedisSecurityStore(_cache);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _cache.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _raw.Dispose();
    }

    private IDatabase Db => _raw.GetDatabase();

    private Task Flush() => _raw.GetServer(_raw.GetEndPoints()[0]).FlushDatabaseAsync();

    private async Task<string?> Raw(string key) => (string?)await Db.StringGetAsync(key);

    /// <summary>Decodifica as duas camadas JSON do Python: texto externo => texto interno.</summary>
    private static string Inner(string raw)
    {
        raw.ShouldStartWith("\"");
        return JsonSerializer.Deserialize<string>(raw)!;
    }

    private async Task Track(int times, DateTime? now = null)
    {
        for (var i = 0; i < times; i++)
        {
            await _store.TrackAttemptAsync(_ip, $"r{i}", now ?? Now, CancellationToken.None);
        }
    }

    [Fact]
    [Trait("AC", "009-12")]
    public async Task Block_IsStoredDoublyEncoded_WithTtlOf3600()
    {
        await Track(5);

        var raw = await Raw($"security:blocked:{_ip}");
        raw.ShouldNotBeNull();
        raw.ShouldStartWith("\"{");
        using var inner = JsonDocument.Parse(Inner(raw));
        inner.RootElement.GetProperty("count").GetInt32().ShouldBe(1);
        inner.RootElement.GetProperty("blocked_until").GetString().ShouldBe("2026-10-04T11:00:00.123000");
        inner.RootElement.GetProperty("first_blocked").GetString().ShouldBe("2026-10-04T10:00:00.123000");
        (await Db.KeyTimeToLiveAsync($"security:blocked:{_ip}"))!.Value.TotalSeconds.ShouldBeInRange(3590, 3600);
    }

    [Fact]
    public async Task Attempts_AreStoredDoublyEncoded_WithTtlOf600()
    {
        await Track(2);

        using var inner = JsonDocument.Parse(Inner((await Raw($"security:attempts:{_ip}"))!));
        inner.RootElement.GetArrayLength().ShouldBe(2);
        inner.RootElement[1].GetProperty("reason").GetString().ShouldBe("r1");
        inner.RootElement[1].GetProperty("timestamp").GetString().ShouldBe("2026-10-04T10:00:00.123000");
        (await Db.KeyTimeToLiveAsync($"security:attempts:{_ip}"))!.Value.TotalSeconds.ShouldBeInRange(590, 600);
    }

    [Fact]
    [Trait("AC", "009-24")]
    public async Task Blacklist_IsStoredDoublyEncoded_WithTtlOfOneYear()
    {
        (await _store.BlacklistAsync(_ip, "Manual block via API", Now, CancellationToken.None)).ShouldBeTrue();

        using var inner = JsonDocument.Parse(Inner((await Raw($"security:blacklist:{_ip}"))!));
        inner.RootElement.GetProperty("ip").GetString().ShouldBe(_ip);
        inner.RootElement.GetProperty("reason").GetString().ShouldBe("Manual block via API");
        inner.RootElement.GetProperty("permanent").GetBoolean().ShouldBeTrue();
        (await Db.KeyTimeToLiveAsync($"security:blacklist:{_ip}"))!.Value.TotalSeconds.ShouldBeInRange(31_535_000, 31_536_000);
        (await _store.IsBlacklistedAsync(_ip, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task FifthAttempt_Blocks_AndClearsTheAttempts()
    {
        await Track(4);
        (await _store.IsBlockedAsync(_ip, Now, CancellationToken.None)).ShouldBeFalse();

        var fifth = await _store.TrackAttemptAsync(_ip, "r4", Now, CancellationToken.None);

        fifth.Outcome.ShouldBe(TrackOutcome.Blocked);
        fifth.Attempts.ShouldBe(5);
        fifth.BlockCount.ShouldBe(1);
        fifth.BlockedUntil.ShouldBe(Now.AddMinutes(60));
        (await Db.KeyExistsAsync($"security:attempts:{_ip}")).ShouldBeFalse();
        (await _store.IsBlockedAsync(_ip, Now, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Block_AccumulatesCount_AndRewritesFirstBlocked()
    {
        await Track(5);
        var later = Now.AddMinutes(90);
        await Track(5, later);

        using var inner = JsonDocument.Parse(Inner((await Raw($"security:blocked:{_ip}"))!));
        inner.RootElement.GetProperty("count").GetInt32().ShouldBe(2);
        inner.RootElement.GetProperty("first_blocked").GetString().ShouldBe("2026-10-04T11:30:00.123000");
        inner.RootElement.GetProperty("blocked_until").GetString().ShouldBe("2026-10-04T12:30:00.123000");
    }

    [Fact]
    public async Task ExpiredBlock_DoesNotBlock_AndKeepsTheKey()
    {
        await Track(5);

        (await _store.IsBlockedAsync(_ip, Now.AddMinutes(60), CancellationToken.None)).ShouldBeFalse();
        (await Db.KeyExistsAsync($"security:blocked:{_ip}")).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "009-14")]
    public async Task AttemptsOlderThanTenMinutes_AreDropped()
    {
        await Track(1, Now.AddMinutes(-11));

        var result = await _store.TrackAttemptAsync(_ip, "nova", Now, CancellationToken.None);

        result.Attempts.ShouldBe(1);
        using var inner = JsonDocument.Parse(Inner((await Raw($"security:attempts:{_ip}"))!));
        inner.RootElement.GetArrayLength().ShouldBe(1);
        inner.RootElement[0].GetProperty("reason").GetString().ShouldBe("nova");
    }

    [Theory]
    [Trait("AC", "009-11")]
    [InlineData("isto nao e json")]
    [InlineData("\"{nao fechado\"")]
    [InlineData("\"{\\\"count\\\": 1}\"")]
    [InlineData("\"{\\\"blocked_until\\\": \\\"ontem\\\", \\\"count\\\": 1}\"")]
    [InlineData("\"[1, 2]\"")]
    public async Task CorruptedBlock_IsDeleted_AndNotBlocking(string corrupted)
    {
        await Db.StringSetAsync($"security:blocked:{_ip}", corrupted);

        (await _store.IsBlockedAsync(_ip, Now, CancellationToken.None)).ShouldBeFalse();
        (await Db.KeyExistsAsync($"security:blocked:{_ip}")).ShouldBeFalse();
    }

    [Fact]
    public async Task CorruptedAttempts_AreTreatedAsEmpty()
    {
        await Db.StringSetAsync($"security:attempts:{_ip}", "\"[{\\\"timestamp\\\": \\\"lixo\\\"}]\"");

        (await _store.TrackAttemptAsync(_ip, "x", Now, CancellationToken.None)).Attempts.ShouldBe(1);
    }

    [Theory]
    [Trait("AC", "009-13")]
    [InlineData("blacklist")]
    [InlineData("blocked")]
    [InlineData("attempts")]
    public async Task ValuesWrittenByPython_AreReadByTheStore(string name)
    {
        var fixture = PythonFixture.Case(name);
        await Db.StringSetAsync(fixture.Key, fixture.Raw);
        var ip = fixture.Key[(fixture.Key.LastIndexOf(':') + 1)..];

        switch (name)
        {
            case "blacklist":
                (await _store.IsBlacklistedAsync(ip, CancellationToken.None)).ShouldBeTrue();
                break;
            case "blocked":
                (await _store.IsBlockedAsync(ip, new DateTime(2026, 10, 4, 10, 30, 0), CancellationToken.None)).ShouldBeTrue();
                (await _store.IsBlockedAsync(ip, new DateTime(2026, 10, 4, 11, 0, 1), CancellationToken.None)).ShouldBeFalse();
                break;
            default:
                var result = await _store.TrackAttemptAsync(ip, "nova", new DateTime(2026, 10, 4, 10, 8, 0), CancellationToken.None);
                result.Attempts.ShouldBe(3);
                break;
        }
    }

    [Fact]
    [Trait("AC", "009-13")]
    public async Task StoreWrites_CanBeDecodedWithThePythonFormatAssumptions()
    {
        // O Python lê json.loads(json.loads(raw)): o texto externo precisa ser um JSON de texto cujo conteúdo é objeto.
        await _store.BlacklistAsync(_ip, null, Now, CancellationToken.None);

        using var inner = JsonDocument.Parse(Inner((await Raw($"security:blacklist:{_ip}"))!));
        inner.RootElement.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task RemoveOperations_DeleteTheRightKeys()
    {
        await Track(5);
        await _store.BlacklistAsync(_ip, "x", Now, CancellationToken.None);
        await Track(1);

        (await _store.RemoveTempBlockAsync(_ip, CancellationToken.None)).ShouldBeTrue();
        (await Db.KeyExistsAsync($"security:blocked:{_ip}")).ShouldBeFalse();
        (await Db.KeyExistsAsync($"security:attempts:{_ip}")).ShouldBeFalse();
        (await Db.KeyExistsAsync($"security:blacklist:{_ip}")).ShouldBeTrue();

        (await _store.RemoveFromBlacklistAsync(_ip, CancellationToken.None)).ShouldBeTrue();
        (await _store.RemoveFromBlacklistAsync(_ip, CancellationToken.None)).ShouldBeTrue();
        (await Db.KeyExistsAsync($"security:blacklist:{_ip}")).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "009-23")]
    public async Task Stats_ListSortedByIp_SkipCorruptedEntries_AndFormatTimeRemaining()
    {
        var ip = $"stats-{Guid.NewGuid():N}";
        await Flush();
        await _store.BlacklistAsync($"{ip}-b", "motivo", Now, CancellationToken.None);
        await _store.BlacklistAsync($"{ip}-a", null, Now, CancellationToken.None);
        await Db.StringSetAsync($"security:blacklist:{ip}-lixo", "nao-json");
        await Db.StringSetAsync($"security:blocked:{ip}-x", "\"{\\\"blocked_until\\\": \\\"2026-10-04T10:59:00\\\", \\\"count\\\": 3}\"");
        await Db.StringSetAsync($"security:blocked:{ip}-expirado", "\"{\\\"blocked_until\\\": \\\"2026-10-04T09:00:00\\\", \\\"count\\\": 1}\"");
        await Db.StringSetAsync($"security:blocked:{ip}-lixo", "lixo");
        for (var i = 0; i < 4; i++)
        {
            await _store.TrackAttemptAsync($"{ip}-s", $"motivo {i}", Now.AddSeconds(i), CancellationToken.None);
        }

        var stats = await _store.GetStatsAsync(new DateTime(2026, 10, 4, 10, 0, 0), CancellationToken.None);

        stats.RedisAvailable.ShouldBeTrue();
        stats.Blacklisted.Select(e => e.Ip).ShouldBe([$"{ip}-a", $"{ip}-b"]);
        stats.Blacklisted[0].Reason.ShouldBeNull();
        stats.Blacklisted[1].Reason.ShouldBe("motivo");
        stats.Blacklisted[1].BlacklistedAt.ShouldBe("2026-10-04T10:00:00.123000");
        stats.Blacklisted[1].Permanent.ShouldBeTrue();
        var blocked = stats.Blocked.ShouldHaveSingleItem();
        blocked.Ip.ShouldBe($"{ip}-x");
        blocked.Attempts.ShouldBe(3);
        blocked.BlockedUntil.ShouldBe("2026-10-04T10:59:00");
        blocked.TimeRemaining.ShouldBe("0:59:00");
        var suspicious = stats.Suspicious.ShouldHaveSingleItem();
        suspicious.Ip.ShouldBe($"{ip}-s");
        suspicious.RecentAttempts.ShouldBe(4);
        suspicious.Reasons.ShouldBe(["motivo 1", "motivo 2", "motivo 3"]);
        suspicious.LastAttempt.ShouldBe("2026-10-04T10:00:03.123000");
    }

    [Fact]
    public async Task Stats_ForPythonValues_ReadEverySection()
    {
        await Flush();
        foreach (var name in new[] { "blacklist", "blocked", "attempts" })
        {
            var fixture = PythonFixture.Case(name);
            await Db.StringSetAsync(fixture.Key, fixture.Raw);
        }

        var stats = await _store.GetStatsAsync(new DateTime(2026, 10, 4, 10, 30, 0), CancellationToken.None);

        stats.Blacklisted.ShouldHaveSingleItem().Ip.ShouldBe("192.0.2.10");
        stats.Blocked.ShouldHaveSingleItem().TimeRemaining.ShouldBe("0:30:00");
        stats.Suspicious.ShouldHaveSingleItem().Reasons.ShouldBe(["Unknown endpoint: /x", @"Suspicious URL pattern: /\.env"]);
    }

    [Fact]
    [Trait("AC", "009-10")]
    public async Task WithoutRedis_EverythingIsFailOpen()
    {
        var store = new RedisSecurityStore(new DownCache());

        (await store.IsBlacklistedAsync(_ip, CancellationToken.None)).ShouldBeFalse();
        (await store.IsBlockedAsync(_ip, Now, CancellationToken.None)).ShouldBeFalse();
        (await store.TrackAttemptAsync(_ip, "x", Now, CancellationToken.None)).Outcome.ShouldBe(TrackOutcome.RedisUnavailable);
        (await store.BlacklistAsync(_ip, "x", Now, CancellationToken.None)).ShouldBeFalse();
        (await store.RemoveFromBlacklistAsync(_ip, CancellationToken.None)).ShouldBeFalse();
        (await store.GetStatsAsync(Now, CancellationToken.None)).RedisAvailable.ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "009-10")]
    public async Task WithUnreachableRedis_DoesNotThrow()
    {
        await using var cache = new RedisCache(
            RedisConnectionOptions.Parse("redis://127.0.0.1:1"), new FakeTimeProvider(), new CapturingLogger<RedisCache>());
        var store = new RedisSecurityStore(cache);

        (await store.IsBlockedAsync(_ip, Now, CancellationToken.None)).ShouldBeFalse();
        (await store.TrackAttemptAsync(_ip, "x", Now, CancellationToken.None)).Outcome.ShouldBe(TrackOutcome.RedisUnavailable);
    }

    private sealed class DownCache : ICache
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> SetJsonAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(default(T));

        public Task<JsonElement?> GetJsonElementAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<JsonElement?>(null);

        public Task<bool> SetIfNotExistsAsync(string key, string rawValue, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<IReadOnlyList<string>> KeysByPatternAsync(string pattern, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}

/// <summary>Valores gravados pelo Python (<c>tests/Fixtures/generate_security_fixture.py</c>).</summary>
internal static class PythonFixture
{
    internal sealed record Entry(string Key, string Raw, string Inner);

    public static Entry Case(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "security-redis-fixture.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var item = document.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);
        return new Entry(
            item.GetProperty("key").GetString()!,
            item.GetProperty("raw").GetString()!,
            item.GetProperty("inner").GetString()!);
    }
}



