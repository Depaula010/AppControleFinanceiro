using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Bot;

/// <summary>
/// Logs do cliente do bot (EventIds 10200 a 10299). Só status HTTP, tipo (<c>texto</c>/<c>imagem</c>), tipo de exceção
/// e o número mascarado (4 últimos dígitos). Nunca a mensagem, o corpo da resposta, a chave nem o número completo (RN-010-18).
/// </summary>
internal static partial class BotLog
{
    [LoggerMessage(EventId = 10201, Level = LogLevel.Warning, Message = "BOT_WHATSAPP_URL não configurada; envio de {Kind} para {Number} não realizado.")]
    public static partial void NoUrl(ILogger logger, string kind, string number);

    [LoggerMessage(EventId = 10202, Level = LogLevel.Warning, Message = "Bot respondeu HTTP {Status} ao envio de {Kind} para {Number}.")]
    public static partial void SendRejected(ILogger logger, string kind, int status, string number);

    [LoggerMessage(EventId = 10203, Level = LogLevel.Warning, Message = "Falha ao enviar {Kind} para {Number}: {Error}")]
    public static partial void SendFailed(ILogger logger, string kind, string number, string error);

    [LoggerMessage(EventId = 10204, Level = LogLevel.Information, Message = "Sessão do bot não encontrada (HTTP 404); sessão será resolvida de novo e o envio de {Kind} repetido uma vez.")]
    public static partial void SessionRetry(ILogger logger, string kind);

    [LoggerMessage(EventId = 10205, Level = LogLevel.Warning, Message = "Não foi possível resolver a sessão do bot: {Reason}. Usando o endpoint legado.")]
    public static partial void SessionNotResolved(ILogger logger, string reason);

    [LoggerMessage(EventId = 10206, Level = LogLevel.Information, Message = "Sessão do bot resolvida: {SessionId}.")]
    public static partial void SessionResolved(ILogger logger, string sessionId);

    [LoggerMessage(EventId = 10207, Level = LogLevel.Warning, Message = "Envio de {Kind} interrompido na parte {Part} de {Total} para {Number}.")]
    public static partial void ChunkFailed(ILogger logger, string kind, int part, int total, string number);
}
