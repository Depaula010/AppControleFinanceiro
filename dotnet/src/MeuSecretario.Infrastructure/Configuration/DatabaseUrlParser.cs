using Npgsql;

namespace MeuSecretario.Infrastructure.Configuration;

/// <summary>
/// Converte a <c>DATABASE_URL</c> (formato usado pelo Flask e pelos secrets do GitHub,
/// <c>postgresql://usuario:senha@host:5432/banco</c>, com senha em URL-encoding)
/// para uma connection string do Npgsql.
/// </summary>
public static class DatabaseUrlParser
{
    public static string ToConnectionString(string url)
    {
        var text = url.Trim();
        if (text.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
        {
            text = "postgresql://" + text["postgres://".Length..];
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(uri.Host))
        {
            // Mensagem sem o valor: a URL contém a senha.
            throw new FormatException("DATABASE_URL inválida: esperado postgresql://usuario:senha@host:porta/banco");
        }

        var userInfo = uri.UserInfo;
        var separator = userInfo.IndexOf(':', StringComparison.Ordinal);
        var user = separator < 0 ? userInfo : userInfo[..separator];
        var password = separator < 0 ? string.Empty : userInfo[(separator + 1)..];

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = Uri.UnescapeDataString(user),
            Password = Uri.UnescapeDataString(password),
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Timeout = 10,          // connect_timeout=10 do Flask
            CommandTimeout = 30,
            KeepAlive = 30,
            MaxPoolSize = 20,
        };
        return builder.ConnectionString;
    }
}
