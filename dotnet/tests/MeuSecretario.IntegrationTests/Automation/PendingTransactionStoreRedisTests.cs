using System.Text.Json;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Infrastructure.Automation;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.IntegrationTests.Support;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Automation;

/// <summary>A pendência no Redis real, nas chaves e no formato que o Flask lê e a 012 vai ler (spec 011 §3.1 passo 7).</summary>
[Trait("Category", "Docker")]
public sealed class PendingTransactionStoreRedisTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime, IDisposable
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
    [Trait("AC", "011-011")]
    public async Task WritesThePendingAndTheLastPending_WithThe300SecondsTtl()
    {
        var whatsapp = $"55{Guid.NewGuid():N}"[..13];
        var store = new PendingTransactionStore(_cache, new FixedPendingId("ab12cd34"));
        var data = PendingTransaction.FromApiEndpoint(
            42, 2, "Nubank", "Conta Corrente", 59, null, "Padaria", "pão", "Padaria - pão", -8, 8, "pix", new DateOnly(2026, 10, 5), null).ToData();

        var id = await store.CreateAsync(whatsapp, data, CancellationToken.None);

        id.ShouldBe("ab12cd34");
        var db = _raw.GetDatabase();
        var pending = JsonDocument.Parse((string)(await db.StringGetAsync($"pending_tx:{whatsapp}:ab12cd34"))!).RootElement;
        pending.GetProperty("descricao_final").GetString().ShouldBe("Padaria - pão");
        pending.GetProperty("valor_db").GetDouble().ShouldBe(-8);
        pending.GetProperty("fatura_id").ValueKind.ShouldBe(JsonValueKind.Null);
        pending.GetProperty("budget_warning").ValueKind.ShouldBe(JsonValueKind.Null);
        (await db.KeyTimeToLiveAsync($"pending_tx:{whatsapp}:ab12cd34"))!.Value.TotalSeconds.ShouldBeInRange(295, 300);
        ((string?)await db.StringGetAsync($"last_pending:{whatsapp}")).ShouldBe("\"ab12cd34\"");
        (await db.KeyTimeToLiveAsync($"last_pending:{whatsapp}"))!.Value.TotalSeconds.ShouldBeInRange(295, 300);
    }
}
