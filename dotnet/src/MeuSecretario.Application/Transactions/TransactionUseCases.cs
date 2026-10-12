using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Transactions;

public abstract record CreateTransactionResult
{
    /// <summary>O eco da resposta vem da entrada (<see cref="NewTransactionData"/>), não do banco.</summary>
    public sealed record Created(NewTransactionData Input, PostedTransaction Posted) : CreateTransactionResult;

    public sealed record Invalid(TransactionError Reason) : CreateTransactionResult;

    public sealed record AccountNotFound : CreateTransactionResult;

    public sealed record SubcategoryNotFound : CreateTransactionResult;
}

public abstract record UpdateTransactionResult
{
    public sealed record Updated(UpdatedTransaction Transaction) : UpdateTransactionResult;

    public sealed record Invalid(TransactionError Reason) : UpdateTransactionResult;

    public sealed record NotFound : UpdateTransactionResult;

    public sealed record AccountNotFound : UpdateTransactionResult;

    public sealed record SubcategoryNotFound : UpdateTransactionResult;
}

/// <summary><c>GET /api/transactions</c> (<c>api.py:728-901</c>): a validação dos parâmetros já foi feita pelo <see cref="ListQueryParser"/>.</summary>
public sealed class ListTransactions(ITransactionRepository transactions)
{
    public Task<TransactionPage> ExecuteAsync(int userId, ListFilter filter, CancellationToken cancellationToken) =>
        transactions.ListAsync(userId, filter, cancellationToken);
}

/// <summary><c>POST /api/transactions</c> (<c>api.py:1301-1459</c>): regras 1 a 9 no Domain; 10 e 11 e a gravação no serviço de lançamento.</summary>
public sealed class CreateTransaction(ITransactionPosting posting)
{
    public const string Origin = "api";

    public async Task<CreateTransactionResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        var validation = CreateTransactionRules.Validate(body);
        if (validation is CreateTransactionValidation.Rejected rejected)
        {
            return new CreateTransactionResult.Invalid(rejected.Error);
        }

        var data = ((CreateTransactionValidation.Ok)validation).Data;
        var result = await posting.PostSimpleAsync(
            new PostSimpleTransaction(
                userId, data.AccountId, data.SubcategoryId, data.Kind, data.Amount, data.Description, data.Date,
                data.Notes, data.Consolidated, Origin),
            cancellationToken);

        return result switch
        {
            PostingResult<PostedTransaction>.Ok posted => new CreateTransactionResult.Created(data, posted.Value),
            PostingResult<PostedTransaction>.Failed { Error: PostingError.AccountNotFound } => new CreateTransactionResult.AccountNotFound(),
            PostingResult<PostedTransaction>.Failed { Error: PostingError.SubcategoryNotFound } => new CreateTransactionResult.SubcategoryNotFound(),
            _ => throw new InvalidOperationException("Resultado de lançamento inesperado."),
        };
    }
}

/// <summary>
/// <c>PUT /api/transactions/{id}</c> (<c>api.py:1462-1635</c>). A ordem é a da spec §3.3: corpo, 404, tipo editável
/// (desvio 068), descrição/valor/tipo/data, subcategoria, conta, observações/consolidada, "nenhum campo", gravação.
/// </summary>
public sealed class UpdateTransaction(ITransactionRepository transactions, ITransactionsUnitOfWork unitOfWork)
{
    public async Task<UpdateTransactionResult> ExecuteAsync(int userId, int id, RawBody? body, CancellationToken cancellationToken)
    {
        if (body is null || body.IsEmpty)
        {
            return new UpdateTransactionResult.Invalid(TransactionError.NoDataProvided);
        }

        var current = await transactions.GetAsync(userId, id, cancellationToken);
        if (current is null)
        {
            return new UpdateTransactionResult.NotFound();
        }
        if (!TransactionTypes.IsEditable(current.DbType))
        {
            return new UpdateTransactionResult.Invalid(TransactionError.NotEditable);
        }

        var headResult = UpdateTransactionRules.ReadHead(body, current.DbType, current.Amount);
        if (headResult is HeadResult.Rejected headRejected)
        {
            return new UpdateTransactionResult.Invalid(headRejected.Error);
        }
        var head = ((HeadResult.Ok)headResult).Changes;

        var references = UpdateTransactionRules.ReadReferences(body);
        if (references.SubcategoryId is { } subcategoryId
            && !await transactions.SubcategoryVisibleAsync(userId, subcategoryId, cancellationToken))
        {
            return new UpdateTransactionResult.SubcategoryNotFound();
        }
        if (references.AccountId is { } accountId
            && !await transactions.ActiveAccountOwnedAsync(userId, accountId, cancellationToken))
        {
            return new UpdateTransactionResult.AccountNotFound();
        }

        var tailResult = UpdateTransactionRules.ReadTail(body);
        if (tailResult is TailResult.Rejected tailRejected)
        {
            return new UpdateTransactionResult.Invalid(tailRejected.Error);
        }

        var changes = UpdateTransactionRules.Combine(head, references, ((TailResult.Ok)tailResult).Changes);
        if (changes.IsEmpty)
        {
            return new UpdateTransactionResult.Invalid(TransactionError.NoValidField);
        }

        var updated = changes.AffectsInvoice
            ? await UpdateRelinkingInvoiceAsync(userId, current, changes, cancellationToken)
            : await transactions.UpdateAsync(userId, id, changes, cancellationToken);

        return updated is null ? new UpdateTransactionResult.NotFound() : new UpdateTransactionResult.Updated(updated);
    }

    /// <summary>
    /// Desvio 069: conta, data ou tipo mudaram, então o vínculo de fatura é recalculado com a regra do lançamento, na
    /// mesma transação do <c>UPDATE</c>. Conta atual inativa e não trocada: a fatura fica como está.
    /// </summary>
    private async Task<UpdatedTransaction?> UpdateRelinkingInvoiceAsync(
        int userId, CurrentTransaction current, TransactionChanges changes, CancellationToken cancellationToken)
    {
        await using var session = await unitOfWork.BeginAsync(cancellationToken);

        var accountId = changes.AccountId.IsSet ? changes.AccountId.Value : current.AccountId;
        var type = changes.Type.IsSet ? changes.Type.Value : current.DbType;
        var date = changes.Date.IsSet ? changes.Date.Value : current.Date;

        var invoice = default(Change<int?>);
        var account = await session.FindActiveAccountAsync(userId, accountId, cancellationToken);
        if (account is not null)
        {
            var resolved = type == TransactionTypes.Expense
                ? await InvoiceLinking.ResolveAsync(session, account, date, cancellationToken)
                : null;
            invoice = Change.Set<int?>(resolved?.Id);
        }

        var updated = await session.UpdateTransactionAsync(userId, current.Id, changes, invoice, cancellationToken);
        if (updated is not null)
        {
            await session.CommitAsync(cancellationToken);
        }
        return updated;
    }
}

/// <summary><c>DELETE /api/transactions/{id}</c> (<c>api.py:1638-1695</c>): a transação, o par e a reabertura da fatura são do repositório.</summary>
public sealed class DeleteTransaction(ITransactionRepository transactions, IClock clock)
{
    /// <returns><c>false</c> = não existe (404).</returns>
    public Task<bool> ExecuteAsync(int userId, int id, CancellationToken cancellationToken) =>
        transactions.DeleteWithPairAsync(userId, id, clock.Today, cancellationToken);
}
