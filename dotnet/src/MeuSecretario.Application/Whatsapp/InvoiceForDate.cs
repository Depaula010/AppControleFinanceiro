using MeuSecretario.Application.Transactions;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// Fatura do ciclo de uma data (<c>get_or_create_fatura</c>): localiza ou cria e <b>comita</b> antes de seguir (a pendência no Redis
/// só nasce depois; desvio 065 via <c>InvoiceLinking</c>). <c>null</c> quando a conta não é cartão com os dois dias.
/// </summary>
public sealed class InvoiceForDate(ITransactionsUnitOfWork unitOfWork) : IInvoiceForDate
{
    public async Task<int?> ResolveAsync(int userId, int accountId, DateOnly day, CancellationToken cancellationToken)
    {
        await using var session = await unitOfWork.BeginAsync(cancellationToken);
        var account = await session.FindActiveAccountAsync(userId, accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var resolved = await InvoiceLinking.ResolveAsync(session, account, day, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return resolved?.Id;
    }
}
