using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>
/// Registro das sub-entregas financeiras da 012 (transações 012a, pagamentos 012b, consultas 012c). Arquivo próprio para que o
/// <see cref="WhatsAppModule"/> só ganhe uma linha (o registro das demais sub-entregas é independente).
/// </summary>
internal static class FinancialWhatsAppModule
{
    public static void AddFinancialServices(IServiceCollection services)
    {
        services.AddSingleton<IFinanceDiagnostics, FinanceDiagnostics>();
        services.AddScoped<IWhatsAppFinanceReader, WhatsAppFinanceReader>();
        services.AddScoped<IFeedbackReader, FeedbackReader>();
        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<IInvoiceForDate, InvoiceForDate>();
        services.AddScoped<AccountResolver>();
        services.AddScoped<TransactionConfirmationSaver>();

        // 012a
        services.AddScoped<IWhatsAppStep, TransactionReplyStep>();
        services.AddScoped<IWhatsAppIntent, IncomeIntent>();
        services.AddScoped<IWhatsAppIntent, ExpenseIntent>();
        services.AddScoped<IWhatsAppIntent, TransferIntent>();
        services.AddScoped<IWhatsAppIntent, InvoicePaymentIntent>();

        // 012b
        services.AddScoped<IFixedBillSettler, FixedBillSettler>();
        services.AddScoped<IWhatsAppStep, PaymentStep>();
    }
}

/// <summary>Logs das intenções financeiras (EventIds 12100 a 12199): só o tipo da exceção, nunca texto do usuário nem valores.</summary>
internal static partial class FinanceLog
{
    [LoggerMessage(EventId = 12100, Level = LogLevel.Warning, Message = "Falha ao montar o feedback da transação: {Error}")]
    public static partial void FeedbackFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 12101, Level = LogLevel.Warning, Message = "Falha ao processar um item do pagamento: {Error}")]
    public static partial void PaymentItemFailed(ILogger logger, string error);
}

internal sealed class FinanceDiagnostics(ILogger<WhatsAppModule> logger) : IFinanceDiagnostics
{
    public void FeedbackFailed(string errorType) => FinanceLog.FeedbackFailed(logger, errorType);

    public void PaymentItemFailed(string errorType) => FinanceLog.PaymentItemFailed(logger, errorType);
}
