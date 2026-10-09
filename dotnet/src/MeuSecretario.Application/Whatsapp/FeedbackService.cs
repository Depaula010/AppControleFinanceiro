using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// <c>gerar_feedback_transacao</c> (<c>transaction_feedback_service.py:353-419</c>, spec 012 §7.8): dados da transação, pote
/// (período do desvio 015 e alerta por limiar) e rodapé de fatura ou saldo. Qualquer exceção devolve o texto simples.
/// </summary>
public sealed class FeedbackService(IFeedbackReader reader, IClock clock, IFinanceDiagnostics diagnostics) : IFeedbackService
{
    public async Task<string> GenerateAsync(int transactionId, CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await reader.FindTransactionAsync(transactionId, cancellationToken)
                              ?? throw new InvalidOperationException("Transação não encontrada.");
            var creditKind = FeedbackMessage.IsCreditKind(transaction.AccountType);
            var today = clock.Today;

            var pot = await reader.FindPotAsync(transaction.UserId, transaction.SubcategoryId, today, cancellationToken);
            var showPot = false;
            if (pot is not null)
            {
                var config = await reader.GetPotAlertConfigAsync(transaction.UserId, cancellationToken);
                showPot = config is null
                          || FeedbackMessage.ShouldShowPot(
                              hasConfig: true,
                              config.Active,
                              config.Threshold ?? throw new InvalidOperationException("Limiar do alerta ausente."),
                              pot.PercentUsed);
            }

            FeedbackFooter? footer = creditKind
                ? await reader.GetInvoiceFooterAsync(transaction.UserId, transaction.AccountId, today, cancellationToken)
                : await reader.GetBalanceFooterAsync(transaction.UserId, transaction.AccountId, cancellationToken);

            return FeedbackMessage.Format(transaction, pot, footer, creditKind, showPot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.FeedbackFailed(ex.GetType().Name);
            return FeedbackMessage.Fallback;
        }
    }
}
