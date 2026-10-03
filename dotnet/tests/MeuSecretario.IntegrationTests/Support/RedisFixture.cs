using Testcontainers.Redis;

namespace MeuSecretario.IntegrationTests.Support;

/// <summary>
/// Redis 7 em container para os testes do <c>ICache</c> (SET NX EX atômico, TTL, formato). Nunca aponta para o Redis
/// de produção. Os testes que usam este fixture exigem Docker (<c>[Trait("Category","Docker")]</c>).
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    /// <summary>Formato do StackExchange.Redis: <c>host:porta</c>.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>Formato do <c>REDIS_URL</c>: <c>redis://host:porta</c>.</summary>
    public string RedisUrl => $"redis://{_container.Hostname}:{_container.GetMappedPublicPort(6379)}";

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
