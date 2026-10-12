using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// Logs do Gemini (EventIds 10300 a 10399). Só prefixo do cache, HIT/MISS, milissegundos, status HTTP, tipo do erro e
/// <c>user_id</c>. Nunca o prompt, o texto da resposta, o corpo de erro nem a chave (RN-010-31, desvio 008).
/// </summary>
internal static partial class LlmLog
{
    [LoggerMessage(EventId = 10301, Level = LogLevel.Information, Message = "[GEMINI-CACHE-HIT] {Prefix} | {Elapsed} ms")]
    public static partial void CacheHit(ILogger logger, string prefix, long elapsed);

    [LoggerMessage(EventId = 10302, Level = LogLevel.Information, Message = "[GEMINI-CACHE-MISS] {Prefix} | {Elapsed} ms | Gemini chamado")]
    public static partial void CacheMiss(ILogger logger, string prefix, long elapsed);

    [LoggerMessage(EventId = 10303, Level = LogLevel.Information, Message = "[GEMINI-CACHE-SAVE] {Prefix} | TTL: {TtlSeconds}s")]
    public static partial void CacheSaved(ILogger logger, string prefix, long ttlSeconds);

    [LoggerMessage(EventId = 10304, Level = LogLevel.Warning, Message = "[GEMINI-CACHE] Redis indisponível; cache ignorado ({Prefix}).")]
    public static partial void CacheBypassed(ILogger logger, string prefix);

    [LoggerMessage(EventId = 10305, Level = LogLevel.Warning, Message = "[GEMINI-CACHE] Erro de cache em {Operation} ({Prefix}): {Error}")]
    public static partial void CacheError(ILogger logger, string operation, string prefix, string error);

    [LoggerMessage(EventId = 10306, Level = LogLevel.Information, Message = "[GEMINI] Chamada concluída em {Elapsed} ms (user_id: {UserId}).")]
    public static partial void Called(ILogger logger, long elapsed, int? userId);

    [LoggerMessage(EventId = 10307, Level = LogLevel.Warning, Message = "[GEMINI] Falha em {Elapsed} ms (user_id: {UserId}): {Error}")]
    public static partial void Failed(ILogger logger, long elapsed, int? userId, string error);

    [LoggerMessage(EventId = 10308, Level = LogLevel.Warning, Message = "[GEMINI] HTTP {Status} do Gemini.")]
    public static partial void HttpStatus(ILogger logger, int status);

    [LoggerMessage(EventId = 10309, Level = LogLevel.Warning, Message = "[GEMINI] Falha de rede ou timeout: {Error}")]
    public static partial void Transport(ILogger logger, string error);

    [LoggerMessage(EventId = 10310, Level = LogLevel.Information, Message = "[CACHE-INVALIDATE] {Deleted} chave(s) apagada(s) (user_id: {UserId}, padrão: {Pattern}).")]
    public static partial void Invalidated(ILogger logger, int deleted, int? userId, string pattern);
}
