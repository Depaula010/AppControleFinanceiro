using MeuSecretario.Application.Categories;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Transactions;

/// <summary>Fatura resolvida para um lançamento (RN-004-09 e 11).</summary>
public sealed record ResolvedInvoice(int Id, DateOnly DueDate);

/// <summary>Localiza ou cria a fatura do ciclo de uma data (RN-004-10 e 11). Compartilhado pelo lançamento e pelo PUT.</summary>
internal static class InvoiceLinking
{
    /// <summary><c>null</c> quando a conta não é cartão ou não tem os dois dias.</summary>
    public static async Task<ResolvedInvoice?> ResolveAsync(
        ITransactionsSession session, PostingAccount account, DateOnly date, CancellationToken cancellationToken)
    {
        if (!account.IsCard || !account.HasCycle)
        {
            return null;
        }

        var cycle = InvoiceCycle.ForPurchase(account.ClosingDay!.Value, account.DueDay!.Value, date);
        var id = await session.FindInvoiceByDueDateAsync(account.Id, cycle.Due, cancellationToken);
        if (id is null)
        {
            id = await session.InsertInvoiceIfMissingAsync(account.Id, cycle.Due, cycle.Closing, cancellationToken)
                 ?? await session.FindInvoiceByDueDateAsync(account.Id, cycle.Due, cancellationToken);
        }

        return id is { } found ? new ResolvedInvoice(found, cycle.Due) : null;
    }
}

/// <summary>
/// Serviço de lançamento (spec 004 §7): a única porta que grava em <c>transacoes</c>, <c>faturas</c> e no agendamento
/// de parcelas pelo caminho de lançamento. Cada método é uma transação do banco; nada é gravado em erro.
/// </summary>
public sealed class TransactionPosting(ITransactionsUnitOfWork unitOfWork, ICategoryLookup categories) : ITransactionPosting
{
    public async Task<PostingResult<PostedTransaction>> PostSimpleAsync(PostSimpleTransaction command, CancellationToken cancellationToken)
    {
        if (command.Amount < 0)
        {
            return Fail<PostedTransaction>(PostingError.AmountInvalid);
        }

        await using var session = await unitOfWork.BeginAsync(cancellationToken);

        var account = await session.FindActiveAccountAsync(command.UserId, command.AccountId, cancellationToken);
        if (account is null)
        {
            return Fail<PostedTransaction>(PostingError.AccountNotFound);
        }
        if (!await session.SubcategoryVisibleAsync(command.UserId, command.SubcategoryId, cancellationToken))
        {
            return Fail<PostedTransaction>(PostingError.SubcategoryNotFound);
        }

        var posted = await InsertSimpleAsync(session, command, account, cancellationToken);
        await session.CommitAsync(cancellationToken);
        return new PostingResult<PostedTransaction>.Ok(posted);
    }

    public async Task<PostingResult<PostedTransfer>> PostTransferAsync(PostTransfer command, CancellationToken cancellationToken)
    {
        if (command.Amount < 0)
        {
            return Fail<PostedTransfer>(PostingError.AmountInvalid);
        }
        if (command.FromAccountId == command.ToAccountId)
        {
            return Fail<PostedTransfer>(PostingError.SameAccount);
        }

        await using var session = await unitOfWork.BeginAsync(cancellationToken);

        var from = await session.FindActiveAccountAsync(command.UserId, command.FromAccountId, cancellationToken);
        if (from is null)
        {
            return Fail<PostedTransfer>(PostingError.AccountNotFound);
        }
        var to = await session.FindActiveAccountAsync(command.UserId, command.ToAccountId, cancellationToken);
        if (to is null)
        {
            return Fail<PostedTransfer>(PostingError.DestinationAccountNotFound);
        }

        var subcategory = await categories.FindShortTermInvestmentAsync(command.UserId, cancellationToken);
        if (subcategory is null)
        {
            return Fail<PostedTransfer>(PostingError.RequiredCategoryMissing, "Investimentos de Curto Prazo");
        }

        var outgoing = await session.InsertTransactionAsync(
            Row(command.UserId, from.Id, subcategory.Value, null, PostingDescriptions.TransferOut(to.Name),
                SignedAmount.Apply(TransactionKind.Expense, command.Amount), TransactionTypes.Transfer, command.Date, command.Origin),
            cancellationToken);
        var incoming = await session.InsertTransactionAsync(
            Row(command.UserId, to.Id, subcategory.Value, null, PostingDescriptions.TransferIn(from.Name),
                SignedAmount.Apply(TransactionKind.Income, command.Amount), TransactionTypes.Transfer, command.Date, command.Origin),
            cancellationToken);
        await session.LinkPairAsync(command.UserId, outgoing.Id, incoming.Id, cancellationToken);

        await session.CommitAsync(cancellationToken);
        return new PostingResult<PostedTransfer>.Ok(new PostedTransfer(outgoing.Id, incoming.Id, from.Name, to.Name));
    }

    public async Task<PostingResult<PostedInvoicePayment>> PostInvoicePaymentAsync(PostInvoicePayment command, CancellationToken cancellationToken)
    {
        if (command.Amount < 0)
        {
            return Fail<PostedInvoicePayment>(PostingError.AmountInvalid);
        }
        if (command.FromAccountId == command.CardAccountId)
        {
            return Fail<PostedInvoicePayment>(PostingError.SameAccount);
        }

        await using var session = await unitOfWork.BeginAsync(cancellationToken);

        var from = await session.FindActiveAccountAsync(command.UserId, command.FromAccountId, cancellationToken);
        if (from is null)
        {
            return Fail<PostedInvoicePayment>(PostingError.AccountNotFound);
        }
        var card = await session.FindActiveAccountAsync(command.UserId, command.CardAccountId, cancellationToken);
        if (card is null)
        {
            return Fail<PostedInvoicePayment>(PostingError.DestinationAccountNotFound);
        }
        if (!card.IsCard)
        {
            return Fail<PostedInvoicePayment>(PostingError.NotACreditCard);
        }

        var subcategory = await categories.FindLoanPrincipalAsync(command.UserId, cancellationToken);
        if (subcategory is null)
        {
            return Fail<PostedInvoicePayment>(PostingError.RequiredCategoryMissing, "Quitação de Empréstimos (Principal)");
        }

        // Desvio 010: paga a Fechada mais antiga; sem ela, a fatura do ciclo da data (criada se faltar).
        ResolvedInvoice? invoice;
        var closed = await session.FindOldestClosedInvoiceAsync(card.Id, cancellationToken);
        invoice = closed is not null
            ? new ResolvedInvoice(closed.Id, closed.DueDate)
            : await InvoiceLinking.ResolveAsync(session, card, command.Date, cancellationToken);

        var outgoing = await session.InsertTransactionAsync(
            Row(command.UserId, from.Id, subcategory.Value, invoice?.Id, PostingDescriptions.PaymentOut(card.Name),
                SignedAmount.Apply(TransactionKind.Expense, command.Amount), TransactionTypes.InvoicePayment, command.Date, command.Origin),
            cancellationToken);
        var incoming = await session.InsertTransactionAsync(
            Row(command.UserId, card.Id, subcategory.Value, invoice?.Id, PostingDescriptions.PaymentIn(from.Name),
                SignedAmount.Apply(TransactionKind.Income, command.Amount), TransactionTypes.InvoicePayment, command.Date, command.Origin),
            cancellationToken);
        await session.LinkPairAsync(command.UserId, outgoing.Id, incoming.Id, cancellationToken);

        // R5: a fatura vira Paga sem comparar o valor pago.
        if (invoice is not null)
        {
            await session.MarkInvoicePaidAsync(invoice.Id, cancellationToken);
        }

        await session.CommitAsync(cancellationToken);
        return new PostingResult<PostedInvoicePayment>.Ok(new PostedInvoicePayment(
            outgoing.Id, incoming.Id, from.Name, card.Name, invoice?.Id, invoice?.DueDate, invoice is not null));
    }

    public async Task<PostingResult<PostedInstallmentPurchase>> PostInstallmentPurchaseAsync(
        PostInstallmentPurchase command, CancellationToken cancellationToken)
    {
        if (command.InstallmentAmount < 0)
        {
            return Fail<PostedInstallmentPurchase>(PostingError.AmountInvalid);
        }
        if (command.TotalInstallments < 2)
        {
            return Fail<PostedInstallmentPurchase>(PostingError.InstallmentsInvalid);
        }

        await using var session = await unitOfWork.BeginAsync(cancellationToken);

        var account = await session.FindActiveAccountAsync(command.UserId, command.AccountId, cancellationToken);
        if (account is null)
        {
            return Fail<PostedInstallmentPurchase>(PostingError.AccountNotFound);
        }
        if (!await session.SubcategoryVisibleAsync(command.UserId, command.SubcategoryId, cancellationToken))
        {
            return Fail<PostedInstallmentPurchase>(PostingError.SubcategoryNotFound);
        }

        var first = await InsertSimpleAsync(
            session,
            new PostSimpleTransaction(
                command.UserId, command.AccountId, command.SubcategoryId, TransactionKind.Expense, command.InstallmentAmount,
                command.Description, command.Date, Origin: command.Origin),
            account,
            cancellationToken);

        var plan = InstallmentSchedule.For(command.Date, command.InstallmentAmount, command.TotalInstallments);
        var scheduleId = await session.InsertInstallmentScheduleAsync(
            new NewInstallmentSchedule(
                command.UserId, command.AccountId, command.SubcategoryId, command.Description, plan.Amount, plan.StartDate,
                plan.DayOfMonth, plan.Total),
            cancellationToken);

        await session.CommitAsync(cancellationToken);
        return new PostingResult<PostedInstallmentPurchase>.Ok(new PostedInstallmentPurchase(first.TransactionId, scheduleId, first.InvoiceId));
    }

    /// <summary>Fatura (só despesa em cartão) e <c>INSERT</c>; as checagens de conta e subcategoria já passaram.</summary>
    private static async Task<PostedTransaction> InsertSimpleAsync(
        ITransactionsSession session, PostSimpleTransaction command, PostingAccount account, CancellationToken cancellationToken)
    {
        var invoice = command.Kind == TransactionKind.Expense
            ? await InvoiceLinking.ResolveAsync(session, account, command.Date, cancellationToken)
            : null;

        var stored = await session.InsertTransactionAsync(
            new NewTransactionRow(
                command.UserId, command.AccountId, command.SubcategoryId, invoice?.Id, command.Description,
                SignedAmount.Apply(command.Kind, command.Amount), TransactionTypes.ToDb(command.Kind), command.Date,
                command.Notes, command.Consolidated, command.Origin, command.ScheduleId),
            cancellationToken);

        return new PostedTransaction(stored.Id, stored.Amount, invoice?.Id, invoice?.DueDate);
    }

    /// <summary>Linha de par: sempre consolidada, sem observações nem agendamento (RN-004-34).</summary>
    private static NewTransactionRow Row(
        int userId, int accountId, int subcategoryId, int? invoiceId, string description, decimal amount, string dbType,
        DateOnly date, string? origin) =>
        new(userId, accountId, subcategoryId, invoiceId, description, amount, dbType, date, null, true, origin, null);

    private static PostingResult<T> Fail<T>(PostingError error, string? detail = null) => new PostingResult<T>.Failed(error, detail);
}
