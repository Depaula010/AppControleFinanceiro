using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Desfecho do pagamento de uma fatura pelo check-in (desvio 264).</summary>
public abstract record CheckinInvoiceOutcome
{
    public sealed record Paid(decimal Amount, string AccountName) : CheckinInvoiceOutcome;

    public sealed record Failed : CheckinInvoiceOutcome;
}

/// <summary>
/// Paga uma fatura citada no check-in pela mesma porta do "paguei a fatura do ..." (<see cref="ITransactionPosting.PostInvoicePaymentAsync"/>):
/// par de <c>Pagamento Fatura</c>, e a fatura quitada vira <c>Paga</c> (R5, desvio 010). Contas de origem: as ativas que não são cartão.
/// </summary>
public sealed class CheckinInvoicePayer(IAutomationLookups lookups, ITransactionPosting posting, IClock clock)
{
    public async Task<IReadOnlyList<AccountRef>> PayingAccountsAsync(int userId, CancellationToken cancellationToken) =>
        [.. (await lookups.ListActiveAccountsAsync(userId, cancellationToken)).Where(a => a.Type != ScheduleRules.CardAccountType)];

    public async Task<CheckinInvoiceOutcome> PayAsync(CheckinInvoiceItem invoice, AccountRef from, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(from);
        if (invoice.CardAccountId <= 0 || !MoneyDecimal.TryFromDouble(invoice.Amount, out var amount) || amount <= 0)
        {
            return new CheckinInvoiceOutcome.Failed();
        }

        var result = await posting.PostInvoicePaymentAsync(
            new PostInvoicePayment(invoice.UserId, from.Id, invoice.CardAccountId, amount, clock.Today), cancellationToken);
        return result is PostingResult<PostedInvoicePayment>.Ok
            ? new CheckinInvoiceOutcome.Paid(amount, from.Name)
            : new CheckinInvoiceOutcome.Failed();
    }
}
