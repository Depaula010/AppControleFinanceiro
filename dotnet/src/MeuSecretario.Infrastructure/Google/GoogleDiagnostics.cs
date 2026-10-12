using MeuSecretario.Application.Google;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MeuSecretario.Infrastructure.Google;

/// <summary>
/// Logs da 010d (EventIds 10400 a 10419). Só id numérico, rótulo fixo e o tipo da exceção (e o <c>SqlState</c>):
/// nunca token, <c>code</c>, <c>state</c>, <c>client_secret</c>, URL do Google, mensagem de exceção nem WhatsApp completo.
/// </summary>
internal static partial class GoogleLog
{
    [LoggerMessage(EventId = 10400, Level = LogLevel.Warning, Message = "Token do Google do usuário {UserId} marcado para reconexão: {Reason}")]
    public static partial void TokenMarkedInvalid(ILogger logger, int userId, string reason);

    [LoggerMessage(EventId = 10401, Level = LogLevel.Warning, Message = "Falha ao renovar o token do Google do usuário {UserId}: {Error}")]
    public static partial void RefreshFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10402, Level = LogLevel.Information, Message = "Notificação de token revogado enviada ao usuário {UserId} ({Number})")]
    public static partial void NotificationSent(ILogger logger, int userId, string number);

    [LoggerMessage(EventId = 10403, Level = LogLevel.Warning, Message = "Falha ao notificar token revogado ao usuário {UserId}: {Error}")]
    public static partial void NotificationFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10404, Level = LogLevel.Warning, Message = "Falha ao revogar o acesso no Google (usuário {UserId}): {Error}")]
    public static partial void RevokeFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10405, Level = LogLevel.Warning, Message = "Falha ao conferir o escopo do Drive (usuário {UserId}): {Error}")]
    public static partial void ScopeCheckFailed(ILogger logger, int userId, string error);
}

public sealed class GoogleDiagnostics(ILogger<GoogleDiagnostics> logger) : IGoogleDiagnostics
{
    public void TokenMarkedInvalid(int userId, string reason) => GoogleLog.TokenMarkedInvalid(logger, userId, reason);

    public void RefreshFailed(int userId, Exception exception) => GoogleLog.RefreshFailed(logger, userId, Describe(exception));

    public void NotificationSent(int userId, string maskedNumber) => GoogleLog.NotificationSent(logger, userId, maskedNumber);

    public void NotificationFailed(int userId, Exception exception) => GoogleLog.NotificationFailed(logger, userId, Describe(exception));

    public void RevokeFailed(int userId, Exception exception) => GoogleLog.RevokeFailed(logger, userId, Describe(exception));

    public void ScopeCheckFailed(int userId, Exception exception) => GoogleLog.ScopeCheckFailed(logger, userId, Describe(exception));

    /// <summary>Só o tipo (e o <c>SqlState</c>): a mensagem pode conter token, host, usuário ou número de WhatsApp (desvio 008).</summary>
    internal static string Describe(Exception exception) =>
        exception is PostgresException pg
            ? $"{exception.GetType().Name} (SqlState {pg.SqlState})"
            : exception.GetType().Name;
}
