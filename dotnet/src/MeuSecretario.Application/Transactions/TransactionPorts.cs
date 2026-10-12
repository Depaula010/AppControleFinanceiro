using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Transactions;

public sealed record TransactionListItem(
    int Id,
    string Description,
    decimal Amount,
    string DbType,
    DateOnly Date,
    string Category,
    string Subcategory,
    string AccountName,
    bool Consolidated,
    int AccountId,
    int SubcategoryId,
    string? Notes);

public sealed record TransactionPage(int Total, IReadOnlyList<TransactionListItem> Items);

/// <summary>Linha atual de uma transação do usuário, lida antes do PUT.</summary>
public sealed record CurrentTransaction(int Id, string DbType, decimal Amount, int AccountId, DateOnly Date);

/// <summary>O que o <c>UPDATE ... RETURNING</c> devolve (valor já arredondado pelo banco).</summary>
public sealed record UpdatedTransaction(int Id, string Description, decimal Amount, string DbType, DateOnly Date);

public sealed record PostingAccount(int Id, string Name, string Type, int? ClosingDay, int? DueDay)
{
    public bool IsCard => Type == AccountTypes.Card;

    public bool HasCycle => ClosingDay is > 0 && DueDay is > 0;
}

public sealed record OpenInvoice(int Id, DateOnly DueDate);

public sealed record NewTransactionRow(
    int UserId,
    int AccountId,
    int SubcategoryId,
    int? InvoiceId,
    string Description,
    decimal Amount,
    string DbType,
    DateOnly Date,
    string? Notes,
    bool Consolidated,
    string? Origin,
    int? ScheduleId);

public sealed record StoredTransaction(int Id, decimal Amount);

public sealed record NewInstallmentSchedule(
    int UserId, int AccountId, int SubcategoryId, string Description, decimal Amount, DateOnly StartDate, int DayOfMonth, int Total);

/// <summary>Leituras e escritas das 4 rotas, cada método com a sua transação. Todo método recebe o <c>userId</c> do token.</summary>
public interface ITransactionRepository
{
    /// <summary><c>COUNT</c> mais a página, na mesma conexão.</summary>
    Task<TransactionPage> ListAsync(int userId, ListFilter filter, CancellationToken cancellationToken);

    /// <summary>Transação do usuário; <c>null</c> = inexistente ou de outro usuário (404).</summary>
    Task<CurrentTransaction?> GetAsync(int userId, int id, CancellationToken cancellationToken);

    /// <summary>Subcategoria global ou do usuário.</summary>
    Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken);

    /// <summary>Conta do usuário e ativa (desvio 039).</summary>
    Task<bool> ActiveAccountOwnedAsync(int userId, int accountId, CancellationToken cancellationToken);

    /// <summary><c>UPDATE</c> só dos campos presentes (mais <c>updated_at</c>); <c>null</c> = a linha sumiu (404).</summary>
    Task<UpdatedTransaction?> UpdateAsync(int userId, int id, TransactionChanges changes, CancellationToken cancellationToken);

    /// <summary>
    /// Apaga a linha e o par (RN-004-15) numa transação; <c>false</c> = não existe (404). Apagar um
    /// <c>Pagamento Fatura</c> reabre a fatura (desvio 070): <paramref name="today"/> decide <c>Fechada</c> ou <c>Aberta</c>.
    /// </summary>
    Task<bool> DeleteWithPairAsync(int userId, int id, DateOnly today, CancellationToken cancellationToken);
}

/// <summary>Uma transação do banco por lançamento (usado pelos serviços de lançamento e pelo PUT que recalcula fatura).</summary>
public interface ITransactionsUnitOfWork
{
    Task<ITransactionsSession> BeginAsync(CancellationToken cancellationToken);
}

/// <summary>Sem <see cref="CommitAsync"/> o descarte faz rollback.</summary>
public interface ITransactionsSession : IAsyncDisposable
{
    Task<PostingAccount?> FindActiveAccountAsync(int userId, int accountId, CancellationToken cancellationToken);

    Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken);

    Task<int?> FindInvoiceByDueDateAsync(int cardAccountId, DateOnly due, CancellationToken cancellationToken);

    /// <summary><c>ON CONFLICT DO NOTHING RETURNING id</c>: <c>null</c> quando a fatura já existe (corrida).</summary>
    Task<int?> InsertInvoiceIfMissingAsync(int cardAccountId, DateOnly due, DateOnly closing, CancellationToken cancellationToken);

    /// <summary>Fatura <c>Fechada</c> de menor vencimento do cartão (desvio 010).</summary>
    Task<OpenInvoice?> FindOldestClosedInvoiceAsync(int cardAccountId, CancellationToken cancellationToken);

    Task<StoredTransaction> InsertTransactionAsync(NewTransactionRow row, CancellationToken cancellationToken);

    /// <summary>Liga <c>transferencia_par_id</c> nos dois sentidos.</summary>
    Task LinkPairAsync(int userId, int firstId, int secondId, CancellationToken cancellationToken);

    Task MarkInvoicePaidAsync(int invoiceId, CancellationToken cancellationToken);

    Task<int> InsertInstallmentScheduleAsync(NewInstallmentSchedule schedule, CancellationToken cancellationToken);

    /// <summary>PUT com recálculo de fatura (desvio 069): <paramref name="invoice"/> só grava se <c>IsSet</c>.</summary>
    Task<UpdatedTransaction?> UpdateTransactionAsync(
        int userId, int id, TransactionChanges changes, Change<int?> invoice, CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PostingAccount>> ListActiveCardsAsync(int userId, int? cardId, CancellationToken cancellationToken);

    /// <summary>Existe fatura <c>Aberta</c> do cartão com esse vencimento.</summary>
    Task<bool> OpenInvoiceExistsAsync(int cardAccountId, DateOnly due, CancellationToken cancellationToken);
}
