using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Support;

[Trait("Category", "Docker")]
public class RedisFixtureTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    [Fact]
    public async Task Fixture_ProvidesAReachableRedisAndARedisUrl()
    {
        redis.RedisUrl.ShouldStartWith("redis://");

        await using var connection = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString);
        var db = connection.GetDatabase();
        (await db.StringSetAsync("k", "v", TimeSpan.FromSeconds(30), When.NotExists)).ShouldBeTrue();
        (await db.StringGetAsync("k")).ToString().ShouldBe("v");
    }
}
