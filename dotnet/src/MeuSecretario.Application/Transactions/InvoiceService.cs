using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Transactions;

/// <summary>
/// <c>ensure_current_invoice_exists</c> (spec 004 §7.6, só a criação): para cada cartão ativo com os dois dias, garante
/// a fatura <c>Aberta</c> do ciclo de hoje (a mesma <see cref="InvoiceCycle.ForPurchase"/> do lançamento, desvio 065).
/// Depois do commit, chama o populador; a falha dele é engolida e só o tipo vai ao diagnóstico.
/// </summary>
public sealed class InvoiceService(
    ITransactionsUnitOfWork unitOfWork,
    IInvoiceSchedulePopulator populator,
    IClock clock,
    ITransactionsDiagnostics diagnostics) : IInvoiceService
{
    public async Task EnsureCurrentAsync(int userId, int? cardAccountId, CancellationToken cancellationToken)
    {
        var created = new List<(int InvoiceId, int CardId, DateOnly Due)>();

        await using (var session = await unitOfWork.BeginAsync(cancellationToken))
        {
            var today = clock.Today;
            foreach (var card in await session.ListActiveCardsAsync(userId, cardAccountId, cancellationToken))
            {
                if (!card.HasCycle)
                {
                    continue;
                }

                var cycle = InvoiceCycle.ForPurchase(card.ClosingDay!.Value, card.DueDay!.Value, today);
                if (await session.OpenInvoiceExistsAsync(card.Id, cycle.Due, cancellationToken))
                {
                    continue;
                }

                var invoiceId = await session.InsertInvoiceIfMissingAsync(card.Id, cycle.Due, cycle.Closing, cancellationToken);
                if (invoiceId is { } id)
                {
                    created.Add((id, card.Id, cycle.Due));
                }
            }

            await session.CommitAsync(cancellationToken);
        }

        foreach (var (invoiceId, cardId, due) in created)
        {
            try
            {
                await populator.PopulateAsync(invoiceId, cardId, due, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.PopulateFailed(invoiceId, ex.GetType().Name);
            }
        }
    }
}
