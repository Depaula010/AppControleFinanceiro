using Dapper;
using MeuSecretario.Application.Auth;
using MeuSecretario.Domain.Auth;
using MeuSecretario.Infrastructure.Auth;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace MeuSecretario.IntegrationTests.Caching;

[Trait("Category", "Docker")]
public sealed class UserCacheCleanerTests(PostgresFixture postgres, RedisFixture redis) : IAsyncLifetime, IClassFixture<RedisFixture>, IDisposable, IClassFixture<PostgresFixture>
{
    private const string Number = "5531999990000";
    private const string Other = "5531888880000";
    private readonly RedisCache _cache = new(RedisConnectionOptions.Parse(redis.RedisUrl), TimeProvider.System, NullLogger<RedisCache>.Instance);

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _cache.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task<int> CreateUserAsync(string whatsapp)
    {
        var repository = new UserRepository(postgres.Connections);
        var result = await repository.CreateWithDefaultAccountsAsync(
            new NewUser("Ana Souza", whatsapp, "pbkdf2:sha256:1000$salt$hash", "chave-cifrada"), DefaultAccounts.For(10, 5), CancellationToken.None);
        return result.UserId!.Value;
    }

    private async Task SeedAsync(string number)
    {
        var ttl = TimeSpan.FromSeconds(300);
        await _cache.SetJsonAsync($"pending_tx:{number}:abc", new { x = 1 }, ttl);
        await _cache.SetJsonAsync($"last_pending:{number}", "abc", ttl);
        await _cache.SetJsonAsync($"pending_event:{number}:e1", new { x = 1 }, ttl);
        await _cache.SetJsonAsync($"nightly_checkin:{number}:c1", new { x = 1 }, ttl);
        await _cache.SetJsonAsync($"nightly_checkin_active:{number}", "c1", ttl);
    }

    [Fact]
    public async Task Clear_RemovesPendingAndCheckinKeysOfTheUser_AndKeepsOthers()
    {
        var userId = await CreateUserAsync(Number);
        await SeedAsync(Number);
        await SeedAsync(Other);
        await _cache.SetJsonAsync($"invoice_closed:{userId}", true, TimeSpan.FromSeconds(300));
        var cleaner = new UserCacheCleaner(postgres.Connections, _cache, NullLogger<UserCacheCleaner>.Instance);

        await cleaner.ClearAsync(userId, CancellationToken.None);

        (await _cache.KeysByPatternAsync($"*{Number}*")).ShouldBeEmpty();
        (await _cache.KeysByPatternAsync($"*{Other}*")).Count.ShouldBe(5);
        (await _cache.ExistsAsync($"invoice_closed:{userId}")).ShouldBeTrue();
    }

    [Fact]
    public async Task Clear_ForAnUnknownUser_DoesNothingAndDoesNotThrow()
    {
        await SeedAsync(Number);
        var cleaner = new UserCacheCleaner(postgres.Connections, _cache, NullLogger<UserCacheCleaner>.Instance);

        await cleaner.ClearAsync(987654, CancellationToken.None);

        (await _cache.KeysByPatternAsync($"*{Number}*")).Count.ShouldBe(5);
        await postgres.DataSource.OpenConnectionAsync(); // banco íntegro
    }
}
