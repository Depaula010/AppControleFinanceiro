namespace MeuSecretario.Api.Auth;

/// <summary>Logs da feature 001. Nunca recebem senha, token, hash, chave nem WhatsApp (RN-001-08).</summary>
internal static partial class AuthLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Erro ao registrar usuário: {Error}")]
    public static partial void RegisterFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Erro ao fazer login: {Error}")]
    public static partial void LoginFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Erro ao verificar token: {Error}")]
    public static partial void VerifyFailed(ILogger logger, string error);
}
