using MeuSecretario.Domain.Automation;
using System.Globalization;

namespace MeuSecretario.Domain.Jobs;

/// <summary>Fatura que o processador fecha (RN-013-50).</summary>
public sealed record ClosedInvoice(
    int Id,
    int AccountId,
    string AccountName,
    DateOnly ClosingDate,
    DateOnly DueDate,
    double Total,
    int UserId,
    string WhatsappNumber);

/// <summary>Fatura fechada que vence em N dias (RN-013-52).</summary>
public sealed record DueSoonInvoice(
    int Id,
    int AccountId,
    string AccountName,
    DateOnly DueDate,
    double Total,
    int UserId,
    string WhatsappNumber,
    int DaysUntilDue);

/// <summary><c>InvoiceNotificationFormatter</c> (<c>invoice_notification_formatter.py</c>): texto literal.</summary>
public static class InvoiceNotificationFormatter
{
    public static string Closed(ClosedInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return $"💳 *FATURA FECHADA*\n\n" +
               $"Cartão: *{invoice.AccountName}*\n" +
               $"Fechamento: {Date(invoice.ClosingDate)}\n" +
               $"Vencimento: {Date(invoice.DueDate)}\n" +
               $"Valor Total: *{MoneyText.Brl(invoice.Total)}*\n\n" +
               "ℹ️ A partir de agora, novas compras entrarão na próxima fatura.";
    }

    public static string DueDateWarning(DueSoonInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var days = invoice.DaysUntilDue;
        var emoji = days <= 1 ? "⚠️" : "🔔";
        var daysText = days == 1 ? "AMANHÃ" : $"em {days.ToString(CultureInfo.InvariantCulture)} dias";
        return $"{emoji} *VENCIMENTO PRÓXIMO*\n\n" +
               $"Cartão: *{invoice.AccountName}*\n" +
               $"Vence: {daysText} ({Date(invoice.DueDate)})\n" +
               $"Valor: *{MoneyText.Brl(invoice.Total)}*\n\n" +
               "💡 Não se esqueça de garantir saldo para o pagamento!";
    }

    /// <summary><c>strftime('%d/%m/%Y')</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
