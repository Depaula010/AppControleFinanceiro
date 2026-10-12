using MeuSecretario.Infrastructure.Caching;

namespace MeuSecretario.UnitTests.Infrastructure.Caching;

public class RedisConnectionOptionsTests
{
    private static string Endpoint(StackExchange.Redis.ConfigurationOptions options) =>
        options.EndPoints.Single() is System.Net.DnsEndPoint dns ? $"{dns.Host}:{dns.Port}" : string.Empty;

    [Fact]
    [Trait("AC", "010-09")]
    public void Parse_PlainUrl_UsesHostAndDefaultPort()
    {
        var options = RedisConnectionOptions.Parse("redis://redis:6379");

        Endpoint(options).ShouldBe("redis:6379");
        options.Password.ShouldBeNull();
        options.User.ShouldBeNull();
        options.Ssl.ShouldBeFalse();
        options.DefaultDatabase.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-09")]
    public void Parse_HostWithoutPort_Uses6379()
    {
        Endpoint(RedisConnectionOptions.Parse("redis://cache.internal")).ShouldBe("cache.internal:6379");
    }

    [Fact]
    [Trait("AC", "010-09")]
    public void Parse_PasswordPortAndDatabase()
    {
        var options = RedisConnectionOptions.Parse("redis://:s%40nha@host:6380/2");

        Endpoint(options).ShouldBe("host:6380");
        options.Password.ShouldBe("s@nha");
        options.User.ShouldBeNull();
        options.DefaultDatabase.ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-09")]
    public void Parse_UserAndPassword()
    {
        var options = RedisConnectionOptions.Parse("redis://usuario:segredo@host");

        options.User.ShouldBe("usuario");
        options.Password.ShouldBe("segredo");
    }

    [Fact]
    [Trait("AC", "010-09")]
    public void Parse_Rediss_EnablesTls_AndDefaultsPort()
    {
        var options = RedisConnectionOptions.Parse("rediss://host");

        options.Ssl.ShouldBeTrue();
        Endpoint(options).ShouldBe("host:6379");
    }

    [Fact]
    public void Parse_AppliesFailOpenTimeouts()
    {
        var options = RedisConnectionOptions.Parse("redis://host:6379");

        options.AbortOnConnectFail.ShouldBeFalse();
        options.ConnectTimeout.ShouldBe(2000);
        options.AsyncTimeout.ShouldBe(2000);
        options.SyncTimeout.ShouldBe(2000);
    }

    [Theory]
    [Trait("AC", "010-09")]
    [InlineData("http://:topsecret@host:6379")]
    [InlineData("redis://:topsecret@:6379")]
    [InlineData("redis://:topsecret@host:6379/abc")]
    [InlineData("topsecret-sem-esquema")]
    [InlineData("")]
    public void Parse_InvalidValue_ThrowsWithoutEchoingTheValue(string value)
    {
        var ex = Should.Throw<FormatException>(() => RedisConnectionOptions.Parse(value));

        ex.Message.ShouldNotContain("topsecret");
        ex.Message.ShouldContain("REDIS_URL");
    }
}

