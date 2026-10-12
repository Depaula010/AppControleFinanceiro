using MeuSecretario.Application.Transactions;
using MeuSecretario.Api.Http;

namespace MeuSecretario.Api.Transactions;

/// <summary>Logs da feature 004. Só ids e o tipo da exceção: nunca descrição, valor, corpo nem token (RN-004-16).</summary>
internal static partial class TransactionsLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "Erro ao carregar transações (usuário {UserId}): {Error}")]
    public static partial void ListFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error, Message = "Erro ao criar transação (usuário {UserId}): {Error}")]
    public static partial void CreateFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Error, Message = "Erro ao atualizar transação {TransactionId} (usuário {UserId}): {Error}")]
    public static partial void UpdateFailed(ILogger logger, int userId, int transactionId, string error);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Error, Message = "Erro ao deletar transação {TransactionId} (usuário {UserId}): {Error}")]
    public static partial void DeleteFailed(ILogger logger, int userId, int transactionId, string error);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Warning, Message = "Falha ao popular a fatura {InvoiceId} depois de criada: {Error}")]
    public static partial void PopulateFailed(ILogger logger, int invoiceId, string error);
}

/// <summary>Implementação do diagnóstico da Application com o logger da Api.</summary>
internal sealed class TransactionsDiagnostics(ILogger<TransactionsModule> logger) : ITransactionsDiagnostics
{
    public void PopulateFailed(int invoiceId, string errorType) => TransactionsLog.PopulateFailed(logger, invoiceId, errorType);
}
