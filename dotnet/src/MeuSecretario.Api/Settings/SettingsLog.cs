namespace MeuSecretario.Api.Settings;

/// <summary>Logs da feature 007. Só a operação, o id do usuário e a descrição segura do erro; nunca corpo, chave, e-mail, endereço ou hora.</summary>
internal static partial class SettingsLog
{
    [LoggerMessage(EventId = 7001, Level = LogLevel.Error, Message = "Erro em configurações do usuário ({Operation}, usuário {UserId}): {Error}")]
    public static partial void Failed(ILogger logger, string operation, int userId, string error);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning, Message = "Provedor de chave indisponível na validação (usuário {UserId}): {Error}")]
    public static partial void ProviderUnavailable(ILogger logger, int userId, string error);
}
