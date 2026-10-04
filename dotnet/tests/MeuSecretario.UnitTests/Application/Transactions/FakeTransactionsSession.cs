using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.UnitTests.Application.Transactions;

/// <summary>Sessão em memória: registra o que foi gravado e se houve commit; pode falhar no N-ésimo comando.</summary>
internal sealed class FakeTransactionsSession : ITransactionsSession
{
    public const int UserId = 42;

    public List<(int UserId, PostingAccount Account)> Accounts { get; } = [];

    public HashSet<int> VisibleSubcategories { get; } = [7];

    public List<FakeInvoice> Invoices { get; } = [];

    public List<NewTransactionRow> Inserted { get; } = [];

    public List<(int First, int Second)> Links { get; } = [];

    public List<NewInstallmentSchedule> Schedules { get; } = [];

    public List<int> PaidInvoices { get; } = [];

    public List<string> Calls { get; } = [];

    public bool Committed { get; private set; }

    public bool Disposed { get; private set; }

    /// <summary>Número (1-based) do comando de escrita que lança <see cref="InvalidOperationException"/>; 0 = nunca.</summary>
    public int FailOnWrite { get; set; }

    public int NextInvoiceId { get; set; } = 100;

    private int _writes;
    private int _nextTransactionId = 500;

    public sealed class FakeInvoice(int id, int cardId, DateOnly due, DateOnly closing, string status)
    {
        public int Id { get; } = id;

        public int CardId { get; } = cardId;

        public DateOnly Due { get; } = due;

        public DateOnly Closing { get; } = closing;

        public string Status { get; set; } = status;
    }

    public FakeTransactionsSession AddAccount(int id, string name, string type = "Conta Corrente", int? closing = null, int? due = null, int userId = UserId)
    {
        Accounts.Add((userId, new PostingAccount(id, name, type, closing, due)));
        return this;
    }

    public FakeInvoice AddInvoice(int cardId, string due, string closing, string status = "Aberta", int? id = null)
    {
        var invoice = new FakeInvoice(id ?? NextInvoiceId++, cardId, Day(due), Day(closing), status);
        Invoices.Add(invoice);
        return invoice;
    }

    public static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public Task<PostingAccount?> FindActiveAccountAsync(int userId, int accountId, CancellationToken cancellationToken)
    {
        Calls.Add($"account:{accountId}");
        return Task.FromResult(Accounts.Where(a => a.UserId == userId && a.Account.Id == accountId).Select(a => a.Account).FirstOrDefault());
    }

    public Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken)
    {
        Calls.Add($"subcategory:{subcategoryId}");
        return Task.FromResult(VisibleSubcategories.Contains(subcategoryId));
    }

    public Task<int?> FindInvoiceByDueDateAsync(int cardAccountId, DateOnly due, CancellationToken cancellationToken) =>
        Task.FromResult(Invoices.Where(i => i.CardId == cardAccountId && i.Due == due).Select(i => (int?)i.Id).FirstOrDefault());

    public Task<int?> InsertInvoiceIfMissingAsync(int cardAccountId, DateOnly due, DateOnly closing, CancellationToken cancellationToken)
    {
        Write();
        if (Invoices.Any(i => i.CardId == cardAccountId && i.Due == due))
        {
            return Task.FromResult<int?>(null);
        }
        var invoice = new FakeInvoice(NextInvoiceId++, cardAccountId, due, closing, "Aberta");
        Invoices.Add(invoice);
        return Task.FromResult<int?>(invoice.Id);
    }

    public Task<OpenInvoice?> FindOldestClosedInvoiceAsync(int cardAccountId, CancellationToken cancellationToken) =>
        Task.FromResult(Invoices
            .Where(i => i.CardId == cardAccountId && i.Status == "Fechada")
            .OrderBy(i => i.Due).ThenBy(i => i.Id)
            .Select(i => new OpenInvoice(i.Id, i.Due))
            .FirstOrDefault());

    public Task<StoredTransaction> InsertTransactionAsync(NewTransactionRow row, CancellationToken cancellationToken)
    {
        Write();
        Inserted.Add(row);
        return Task.FromResult(new StoredTransaction(_nextTransactionId++, Math.Round(row.Amount, 2)));
    }

    public Task LinkPairAsync(int userId, int firstId, int secondId, CancellationToken cancellationToken)
    {
        Write();
        Links.Add((firstId, secondId));
        return Task.CompletedTask;
    }

    public Task MarkInvoicePaidAsync(int invoiceId, CancellationToken cancellationToken)
    {
        Write();
        PaidInvoices.Add(invoiceId);
        Invoices.First(i => i.Id == invoiceId).Status = "Paga";
        return Task.CompletedTask;
    }

    public Task<int> InsertInstallmentScheduleAsync(NewInstallmentSchedule schedule, CancellationToken cancellationToken)
    {
        Write();
        Schedules.Add(schedule);
        return Task.FromResult(900 + Schedules.Count);
    }

    public (int UserId, int Id, TransactionChanges Changes, Change<int?> Invoice)? Updated { get; private set; }

    public UpdatedTransaction? UpdateResult { get; set; } = new(1, "x", -1m, "Despesa", new DateOnly(2026, 1, 1));

    public Task<UpdatedTransaction?> UpdateTransactionAsync(
        int userId, int id, TransactionChanges changes, Change<int?> invoice, CancellationToken cancellationToken)
    {
        Write();
        Updated = (userId, id, changes, invoice);
        return Task.FromResult(UpdateResult);
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PostingAccount>> ListActiveCardsAsync(int userId, int? cardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PostingAccount>>(Accounts
            .Where(a => a.UserId == userId && a.Account.IsCard && (cardId is null || a.Account.Id == cardId))
            .Select(a => a.Account)
            .ToList());

    public Task<bool> OpenInvoiceExistsAsync(int cardAccountId, DateOnly due, CancellationToken cancellationToken) =>
        Task.FromResult(Invoices.Any(i => i.CardId == cardAccountId && i.Due == due && i.Status == "Aberta"));

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    private void Write()
    {
        _writes++;
        if (FailOnWrite == _writes)
        {
            throw new InvalidOperationException("falha injetada");
        }
    }
}

internal sealed class FakeUnitOfWork(FakeTransactionsSession session) : ITransactionsUnitOfWork
{
    public int Begins { get; private set; }

    public Task<ITransactionsSession> BeginAsync(CancellationToken cancellationToken)
    {
        Begins++;
        return Task.FromResult<ITransactionsSession>(session);
    }
}
