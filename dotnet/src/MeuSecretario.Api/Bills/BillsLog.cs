namespace MeuSecretario.Api.Bills;

/// <summary>Logs da feature 005. Só ids e o tipo da exceção: nunca descrição, valor, corpo nem token (RN-005-14).</summary>
internal static partial class BillsLog
{
    [LoggerMessage(EventId = 5001, Level = LogLevel.Error, Message = "Erro ao carregar contas mensais (usuário {UserId}): {Error}")]
    public static partial void ListFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Error, Message = "Erro ao criar conta mensal (usuário {UserId}): {Error}")]
    public static partial void CreateFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Error, Message = "Erro ao atualizar conta mensal {BillId} (usuário {UserId}): {Error}")]
    public static partial void UpdateFailed(ILogger logger, int userId, int billId, string error);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Error, Message = "Erro ao remover conta mensal {BillId} (usuário {UserId}): {Error}")]
    public static partial void DeleteFailed(ILogger logger, int userId, int billId, string error);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Error, Message = "Erro ao calcular resumo de contas mensais (usuário {UserId}): {Error}")]
    public static partial void SummaryFailed(ILogger logger, int userId, string error);
}
