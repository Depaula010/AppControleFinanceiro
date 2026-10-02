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

    [LoggerMessage(EventId = 8010, Level = LogLevel.Error, Message = "Erro ao registrar consentimento: {Error}")]
    public static partial void RegisterConsentFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 8011, Level = LogLevel.Error, Message = "Erro ao solicitar consentimentos (usuário {UserId}): {Error}")]
    public static partial void InitialConsentsFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8012, Level = LogLevel.Error, Message = "Erro ao exportar dados (usuário {UserId}): {Error}")]
    public static partial void ExportFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8013, Level = LogLevel.Error, Message = "Erro ao deletar conta (usuário {UserId}): {Error}")]
    public static partial void DeleteAccountFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 8014, Level = LogLevel.Information, Message = "Conta do usuário {UserId} deletada por pedido LGPD")]
    public static partial void AccountDeleted(ILogger logger, int userId);

    [LoggerMessage(EventId = 8015, Level = LogLevel.Information, Message = "Exportação LGPD dos dados do usuário {UserId}")]
    public static partial void DataExported(ILogger logger, int userId);

    [LoggerMessage(EventId = 8009, Level = LogLevel.Error, Message = "Erro ao consultar logs de acesso (usuário {UserId}): {Error}")]
    public static partial void LogsFailed(ILogger logger, int userId, string error);
}
