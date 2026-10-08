using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Estado da conversa no Redis real, nas chaves e no formato que o Flask lê (spec 012 §7.2, §7.3).</summary>
[Trait("Category", "Docker")]
public sealed class WhatsAppStoresRedisTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime, IDisposable
{
    private RedisCache _cache = default!;
    private ConnectionMultiplexer _raw = default!;

    public async Task InitializeAsync()
    {
        _cache = new RedisCache(RedisConnectionOptions.Parse(redis.RedisUrl), TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<RedisCache>.Instance);
        _raw = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _cache.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _raw.Dispose();
    }

    [Fact]
    [Trait("AC", "012-018")]
    public async Task PendingTransaction_IsReadBackAndRewrittenWithThe300SecondsTtl()
    {
        var number = $"55{Guid.NewGuid():N}"[..13];
        var db = _raw.GetDatabase();
        await db.StringSetAsync($"pending_tx:{number}:abc12345", """{"usuario_id": 7, "descricao": "Pão", "valor_db": -50.0}""");
        await db.StringSetAsync($"last_pending:{number}", "\"abc12345\"");
        var state = new PendingTransactionState(_cache);

        (await state.GetLastPendingIdAsync(number, CancellationToken.None)).ShouldBe("abc12345");
        var data = await state.GetAsync(number, "abc12345", CancellationToken.None);
        data!["descricao"]!.GetValue<string>().ShouldBe("Pão");
        data["awaiting_category_selection"] = true;
        await state.SaveAsync(number, "abc12345", data, CancellationToken.None);

        (await db.KeyTimeToLiveAsync($"pending_tx:{number}:abc12345"))!.Value.TotalSeconds.ShouldBeInRange(295, 300);
        await state.DeleteBothAsync(number, "abc12345", CancellationToken.None);
        (await db.KeyExistsAsync($"pending_tx:{number}:abc12345")).ShouldBeFalse();
        (await db.KeyExistsAsync($"last_pending:{number}")).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "012-018")]
    public async Task PendingEvent_IsFoundByThePattern_AndTheCheckinSessionIsReadAndCleared()
    {
        var number = $"55{Guid.NewGuid():N}"[..13];
        var db = _raw.GetDatabase();
        var events = new PendingEventStore(_cache, new FixedPendingId("ev000001"));
        await events.CreateAsync(number, new PendingEvent(7, "Academia", "2026-10-09", "07:00", null, null, null), CancellationToken.None);

        var latest = await events.GetLatestAsync(number, CancellationToken.None);
        latest.Id.ShouldBe("ev000001");
        latest.Data!.Title.ShouldBe("Academia");
        (await db.KeyTimeToLiveAsync($"pending_event:{number}:ev000001"))!.Value.TotalSeconds.ShouldBeInRange(295, 300);

        await db.StringSetAsync($"nightly_checkin_active:{number}", "\"chk00001\"");
        var session = new CheckinSessionState(_cache);
        (await session.GetActiveIdAsync(number, CancellationToken.None)).ShouldBe("chk00001");
        await session.ClearActiveAsync(number, CancellationToken.None);
        (await session.GetActiveIdAsync(number, CancellationToken.None)).ShouldBeNull();
    }
}
