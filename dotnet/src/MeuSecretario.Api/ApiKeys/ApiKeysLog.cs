namespace MeuSecretario.Api.ApiKeys;

/// <summary>Logs da feature 008. Só o erro descrito por <c>SafeError.Describe</c> e ids numéricos: nunca chave, corpo, X-API-KEY ou Authorization.</summary>
internal static partial class ApiKeysLog
{
    [LoggerMessage(EventId = 8001, Level = LogLevel.Error, Message = "Erro ao cadastrar chave de API (usuário {UserId}): {Error}")]
    public static partial void RegisterKeyFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Error, Message = "Erro ao listar chaves de API (usuário {UserId}): {Error}")]
    public static partial void ListKeysFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Error, Message = "Erro ao atualizar chave de API {KeyId}: {Error}")]
    public static partial void UpdateKeyFailed(ILogger logger, int keyId, string error);

    [LoggerMessage(EventId = 8004, Level = LogLevel.Error, Message = "Erro ao deletar chave de API {KeyId}: {Error}")]
    public static partial void DeleteKeyFailed(ILogger logger, int keyId, string error);

    [LoggerMessage(EventId = 8005, Level = LogLevel.Error, Message = "Erro ao configurar preferência (usuário {UserId}): {Error}")]
    public static partial void SetPreferenceFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8006, Level = LogLevel.Error, Message = "Erro ao listar preferências (usuário {UserId}): {Error}")]
    public static partial void ListPreferencesFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8007, Level = LogLevel.Error, Message = "Erro ao deletar preferência (usuário {UserId}): {Error}")]
    public static partial void DeletePreferenceFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8008, Level = LogLevel.Error, Message = "Erro ao consultar uso (usuário {UserId}): {Error}")]
    public static partial void UsageFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8009, Level = LogLevel.Error, Message = "Erro ao consultar logs de acesso (usuário {UserId}): {Error}")]
    public static partial void LogsFailed(ILogger logger, int userId, string error);
}
