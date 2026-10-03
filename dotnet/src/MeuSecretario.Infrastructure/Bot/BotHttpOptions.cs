namespace MeuSecretario.Infrastructure.Bot;

/// <summary>Timeouts e nomes dos <c>HttpClient</c> do bot (spec 010 §5.2). Sem retry de rede: reenviar duplicaria mensagens (RN-010-15).</summary>
public static class BotHttpOptions
{
    public const string TextClient = "bot-text";
    public const string ImageClient = "bot-image";

    public static readonly TimeSpan TextTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan SessionsTimeout = TimeSpan.FromSeconds(10);

    /// <summary>TTL do cache <c>bot_session_id:{nome}</c>.</summary>
    public static readonly TimeSpan SessionCacheTtl = TimeSpan.FromSeconds(600);
}
