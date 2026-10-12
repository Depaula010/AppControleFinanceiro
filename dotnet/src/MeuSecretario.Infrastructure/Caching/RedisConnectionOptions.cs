using System.Globalization;
using StackExchange.Redis;

namespace MeuSecretario.Infrastructure.Caching;

/// <summary>
/// Converte <c>REDIS_URL</c> (<c>redis://[usuario]:[senha]@host[:porta][/db]</c> ou <c>rediss://</c>) em opções do
/// StackExchange.Redis (RN-010-08). A mensagem de erro nunca contém o valor, que pode ter senha.
/// </summary>
public static class RedisConnectionOptions
{
    private const int DefaultPort = 6379;
    private const int TimeoutMilliseconds = 2000;

    public static ConfigurationOptions Parse(string redisUrl)
    {
        if (!Uri.TryCreate(redisUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "redis" && uri.Scheme != "rediss")
            || string.IsNullOrEmpty(uri.Host))
        {
            throw Invalid();
        }

        var options = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            ConnectTimeout = TimeoutMilliseconds,
            AsyncTimeout = TimeoutMilliseconds,
            SyncTimeout = TimeoutMilliseconds,
            ConnectRetry = 1,
            Ssl = uri.Scheme == "rediss",
            BacklogPolicy = BacklogPolicy.FailFast,
            ReconnectRetryPolicy = new ExponentialRetry(500, 5000),
        };
        options.EndPoints.Add(uri.Host, uri.Port > 0 ? uri.Port : DefaultPort);

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var separator = uri.UserInfo.IndexOf(':', StringComparison.Ordinal);
            var user = separator < 0 ? uri.UserInfo : uri.UserInfo[..separator];
            var password = separator < 0 ? string.Empty : uri.UserInfo[(separator + 1)..];
            if (user.Length > 0)
            {
                options.User = Uri.UnescapeDataString(user);
            }

            if (password.Length > 0)
            {
                options.Password = Uri.UnescapeDataString(password);
            }
        }

        var path = uri.AbsolutePath.Trim('/');
        if (path.Length > 0)
        {
            if (!int.TryParse(path, NumberStyles.None, CultureInfo.InvariantCulture, out var database))
            {
                throw Invalid();
            }

            options.DefaultDatabase = database;
        }

        return options;
    }

    private static FormatException Invalid() =>
        new("REDIS_URL inválida: use redis://[usuario]:[senha]@host[:porta][/db] ou rediss://.");
}
