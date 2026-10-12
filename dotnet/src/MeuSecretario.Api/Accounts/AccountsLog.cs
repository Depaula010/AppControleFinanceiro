namespace MeuSecretario.Api.Accounts;

/// <summary>Logs da feature 002. Nunca recebem nome de conta, saldo, corpo nem token (RN-002-19).</summary>
internal static partial class AccountsLog
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "Erro ao carregar contas (usuário {UserId}): {Error}")]
    public static partial void ListFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "Erro ao criar conta (usuário {UserId}): {Error}")]
    public static partial void CreateFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error, Message = "Erro ao atualizar conta {AccountId} (usuário {UserId}): {Error}")]
    public static partial void UpdateFailed(ILogger logger, int userId, int accountId, string error);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Error, Message = "Erro ao remover conta {AccountId} (usuário {UserId}): {Error}")]
    public static partial void DeleteFailed(ILogger logger, int userId, int accountId, string error);
}
