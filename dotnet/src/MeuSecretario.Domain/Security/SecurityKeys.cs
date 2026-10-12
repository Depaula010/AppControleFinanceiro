namespace MeuSecretario.Domain.Security;

/// <summary>Chaves e TTLs do Redis, idênticos ao Flask (RN-009-09).</summary>
public static class SecurityKeys
{
    public const string BlockedPrefix = "security:blocked:";
    public const string AttemptsPrefix = "security:attempts:";
    public const string BlacklistPrefix = "security:blacklist:";

    public static readonly TimeSpan BlockedTtl = TimeSpan.FromSeconds(3600);
    public static readonly TimeSpan AttemptsTtl = TimeSpan.FromSeconds(600);
    public static readonly TimeSpan BlacklistTtl = TimeSpan.FromSeconds(31_536_000);

    public static string Blocked(string ip) => BlockedPrefix + ip;

    public static string Attempts(string ip) => AttemptsPrefix + ip;

    public static string Blacklist(string ip) => BlacklistPrefix + ip;
}
