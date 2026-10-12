using MeuSecretario.Infrastructure.Configuration;
using Npgsql;

namespace MeuSecretario.UnitTests.Configuration;

public class DatabaseUrlTests
{
    [Fact]
    public void ToConnectionString_DecodesPercentEncodedPassword()
    {
        // Mesmos caracteres especiais da senha real de produção (^ $ # &), valor fictício.
        var url = "postgresql://app_user:a%5Eb%24c!d%23e%26f@postgres-central:5432/bot_whatsapp_prod";

        var builder = new NpgsqlConnectionStringBuilder(DatabaseUrlParser.ToConnectionString(url));

        builder.Host.ShouldBe("postgres-central");
        builder.Port.ShouldBe(5432);
        builder.Username.ShouldBe("app_user");
        builder.Password.ShouldBe("a^b$c!d#e&f");
        builder.Database.ShouldBe("bot_whatsapp_prod");
    }

    [Theory]
    [InlineData("postgres://u:p@h:5432/db")]
    [InlineData("POSTGRESQL://u:p@h:5432/db")]
    public void ToConnectionString_AcceptsLegacySchemeAndCase(string url)
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseUrlParser.ToConnectionString(url));

        builder.Host.ShouldBe("h");
        builder.Database.ShouldBe("db");
    }

    [Fact]
    public void ToConnectionString_DefaultsPortTo5432()
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseUrlParser.ToConnectionString("postgresql://u:p@h/db"));

        builder.Port.ShouldBe(5432);
    }

    [Fact]
    public void ToConnectionString_AppliesConnectTimeoutOf10Seconds()
    {
        var builder = new NpgsqlConnectionStringBuilder(DatabaseUrlParser.ToConnectionString("postgresql://u:p@h/db"));

        builder.Timeout.ShouldBe(10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("mysql://u:p@h/db")]
    [InlineData("not a url")]
    [InlineData("postgresql:///db")]
    public void ToConnectionString_RejectsInvalidUrl(string url)
    {
        Should.Throw<FormatException>(() => DatabaseUrlParser.ToConnectionString(url));
    }

    [Fact]
    public void ToConnectionString_ErrorMessageNeverContainsTheUrl()
    {
        var secret = "senha-super-secreta";

        var ex = Should.Throw<FormatException>(() => DatabaseUrlParser.ToConnectionString($"mysql://u:{secret}@h/db"));

        ex.Message.ShouldNotContain(secret);
    }
}
