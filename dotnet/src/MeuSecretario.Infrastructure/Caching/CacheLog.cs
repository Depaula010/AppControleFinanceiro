using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Caching;

/// <summary>Logs do cache (EventIds 10100 a 10199). Nunca recebem valor, chave com telefone completo nem mensagem de exceção (RN-010-09).</summary>
internal static partial class CacheLog
{
    [LoggerMessage(EventId = 10101, Level = LogLevel.Warning, Message = "Redis indisponível; operação {Operation} devolveu o valor neutro (aviso limitado a 1 por minuto).")]
    public static partial void Unavailable(ILogger logger, string operation);

    [LoggerMessage(EventId = 10102, Level = LogLevel.Warning, Message = "Falha na operação {Operation} do Redis: {Error} (aviso limitado a 1 por minuto).")]
    public static partial void OperationFailed(ILogger logger, string operation, string error);

    [LoggerMessage(EventId = 10103, Level = LogLevel.Information, Message = "Limpeza do cache do usuário {UserId}: {Deleted} chave(s) apagada(s).")]
    public static partial void UserCacheCleared(ILogger logger, int userId, int deleted);

    [LoggerMessage(EventId = 10104, Level = LogLevel.Information, Message = "Limpeza do cache do usuário {UserId} ignorada: o número de WhatsApp não está mais disponível.")]
    public static partial void UserCacheSkipped(ILogger logger, int userId);
}
