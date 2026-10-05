namespace MeuSecretario.Api.Automation;

/// <summary>Logs da feature 011. Só o tipo da exceção: nunca chave do usuário, corpo, descrição, valor, prompt nem número de WhatsApp (RN-011-05).</summary>
internal static partial class AutomationLog
{
    [LoggerMessage(EventId = 11001, Level = LogLevel.Error, Message = "Erro no /webhook-automate: {Error}")]
    public static partial void AutomateFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 11002, Level = LogLevel.Error, Message = "Erro no /api/transacao: {Error}")]
    public static partial void ApiTransactionFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 11003, Level = LogLevel.Error, Message = "Erro no /webhook-sms-payment: {Error}")]
    public static partial void SmsFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 11004, Level = LogLevel.Error, Message = "Erro ao atualizar a reserva do agendamento {ScheduleId}: {Error}")]
    public static partial void ToggleFailed(ILogger logger, int scheduleId, string error);

    [LoggerMessage(EventId = 11005, Level = LogLevel.Error, Message = "Erro ao listar a reserva: {Error}")]
    public static partial void ListFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 11006, Level = LogLevel.Error, Message = "Erro ao reprocessar a fatura {InvoiceId}: {Error}")]
    public static partial void ReprocessFailed(ILogger logger, int invoiceId, string error);
}
