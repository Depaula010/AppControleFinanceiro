using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Application.Automation;

/// <summary>Dono de uma chave do atalho (<c>get_user_by_api_key</c>): id e WhatsApp.</summary>
public sealed record AutomationUser(int UserId, string WhatsApp);

/// <summary>Autentica pela <c>api_key_automate</c> do usuário (spec 011 §3.0). Chave ausente ou desconhecida devolve <c>null</c>.</summary>
public interface IUserApiKeyAuthenticator
{
    Task<AutomationUser?> AuthenticateAsync(string? key, CancellationToken cancellationToken);
}

public sealed record StoredUserKey(int UserId, string WhatsApp, string StoredKey);

/// <summary>Usuários com chave (a coluna guarda texto puro legado ou Fernet).</summary>
public interface IUserKeyReader
{
    Task<IReadOnlyList<StoredUserKey>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Agendamento escolhido para quitação: descrição, conta e subcategoria (<c>settle_fixed_bill</c>).</summary>
public sealed record SettleTarget(string Description, int AccountId, int SubcategoryId);

/// <summary>Leituras de contas, categorias e contas fixas (spec 011 §7.3), sempre só contas ativas do usuário.</summary>
public interface IAutomationLookups
{
    /// <summary>Contas ativas do usuário, <c>ORDER BY id</c> (desvio 039).</summary>
    Task<IReadOnlyList<AccountRef>> ListActiveAccountsAsync(int userId, CancellationToken cancellationToken);

    /// <summary><c>get_account_details_by_name</c>: nome exato, depois <c>ILIKE</c> no nome, depois <c>ILIKE</c> no tipo.</summary>
    Task<AccountRef?> FindAccountByNameOrTypeAsync(int userId, string name, CancellationToken cancellationToken);

    /// <summary><c>get_account_by_name(..., fallback=True)</c>: exato, depois <c>ILIKE</c> (menor id), depois a primeira conta.</summary>
    Task<int?> FindAccountIdWithFallbackAsync(int userId, string name, CancellationToken cancellationToken);

    /// <summary><c>get_user_categories</c>: <paramref name="income"/> escolhe o grupo <c>Renda</c>; senão os de despesa. <c>ORDER BY s.id</c>.</summary>
    Task<IReadOnlyList<CategoryOption>> ListCategoriesAsync(int userId, bool income, CancellationToken cancellationToken);

    /// <summary><c>get_category_name_by_id</c>: <c>Macro -&gt; Sub</c> ou <c>null</c> (o chamador escreve "Categoria Desconhecida").</summary>
    Task<string?> GetCategoryLabelAsync(int subcategoryId, CancellationToken cancellationToken);

    /// <summary>Agendamentos ativos <c>FIXO</c>/<c>LEMBRETE_VARIAVEL</c> do usuário, <c>ORDER BY id</c>.</summary>
    Task<IReadOnlyList<FixedBill>> ListFixedBillsAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Agendamento do usuário (qualquer estado) para a quitação; <c>null</c> = não existe.</summary>
    Task<SettleTarget?> FindSettleTargetAsync(int userId, int scheduleId, CancellationToken cancellationToken);
}

/// <summary>Potes ativos e gasto do período (<c>budget_validation_service</c>).</summary>
public interface IBudgetReader
{
    Task<IReadOnlyList<PotRow>> ListPotsForSubcategoryAsync(int userId, int subcategoryId, CancellationToken cancellationToken);

    /// <summary><c>abs(COALESCE(SUM(valor), 0))</c> das <c>Despesa</c> do usuário nas subcategorias do pote, período inclusivo.</summary>
    Task<decimal> SumPotSpendAsync(int userId, int potId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken);
}

/// <summary>Agendamento ativo lido para o PATCH da reserva.</summary>
public sealed record ReserveScheduleRow(string Description, decimal? ExpectedAmount, string Periodicity, string ScheduleType);

public sealed record ReserveListItem(
    int Id,
    string Description,
    decimal? ExpectedAmount,
    string Periodicity,
    string ScheduleType,
    int DayOfMonth,
    string Category,
    string MacroCategory,
    string Group,
    bool? IncludeInReserve);

public sealed record ReserveListPage(long Total, IReadOnlyList<ReserveListItem> Items);

public interface IReserveScheduleStore
{
    /// <summary>Agendamento do usuário, <b>ativo</b>, com esse id; <c>null</c> = 404.</summary>
    Task<ReserveScheduleRow?> FindActiveScheduleAsync(int userId, int scheduleId, CancellationToken cancellationToken);

    Task SetIncludeInReserveAsync(int userId, int scheduleId, bool include, CancellationToken cancellationToken);

    /// <summary><c>COUNT</c> e página na mesma conexão (spec 011 §3.5).</summary>
    Task<ReserveListPage> ListAsync(int userId, ReserveListFilter filter, CancellationToken cancellationToken);
}

/// <summary>Fatura a reprocessar: cartão, vencimento e status.</summary>
public sealed record ReprocessTarget(int InvoiceId, int CardAccountId, string CardName, DateOnly DueDate, string Status);

/// <summary>Agendamento a lançar na fatura (<c>process_invoice_schedules</c>).</summary>
public sealed record ScheduleToPost(
    int ScheduleId,
    int UserId,
    string ScheduleType,
    string Description,
    decimal? ExpectedAmount,
    int DayOfMonth,
    int SubcategoryId,
    int? TotalInstallments,
    int? InstallmentsExecuted,
    string GroupName);

public sealed record NewScheduledTransaction(
    int UserId, int AccountId, int SubcategoryId, int InvoiceId, int ScheduleId, string Description, decimal Amount, string DbType, DateOnly Date);

/// <summary>Uma transação do banco para processar uma fatura; sem <see cref="CommitAsync"/> o descarte desfaz tudo.</summary>
public interface IInvoiceScheduleSession : IAsyncDisposable
{
    /// <summary>Ativos da conta do cartão; <c>ANUAL</c> só com <c>mes_execucao</c> = <paramref name="referenceMonth"/>; ordem <c>dia_execucao, descricao</c>.</summary>
    Task<IReadOnlyList<ScheduleToPost>> ListSchedulesAsync(int cardAccountId, int referenceMonth, CancellationToken cancellationToken);

    Task<bool> TransactionExistsAsync(int invoiceId, int scheduleId, CancellationToken cancellationToken);

    Task InsertTransactionAsync(NewScheduledTransaction row, CancellationToken cancellationToken);

    Task AdvanceInstallmentAsync(int scheduleId, int newInstallmentsExecuted, bool active, CancellationToken cancellationToken);

    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IInvoiceScheduleUnitOfWork
{
    Task<IInvoiceScheduleSession> BeginAsync(CancellationToken cancellationToken);

    Task<ReprocessTarget?> FindInvoiceAsync(int invoiceId, CancellationToken cancellationToken);
}

/// <summary><c>process_invoice_schedules</c> (spec 011 §7.7): idempotente por (<c>fatura_id</c>, <c>agendamento_id</c>).</summary>
public interface IInvoiceScheduleProcessor
{
    Task<InvoiceScheduleStats> ProcessAsync(int invoiceId, int cardAccountId, DateOnly referenceDate, CancellationToken cancellationToken);
}

/// <summary>Pendência de confirmação no Redis (<c>TransactionConfirmationService.create_pending_transaction</c>).</summary>
public interface IPendingTransactionStore
{
    /// <summary>
    /// Grava <c>pending_tx:{whatsapp}:{id}</c> (TTL 300 s) e <c>last_pending:{whatsapp}</c>. <c>null</c> = o Redis não gravou.
    /// </summary>
    Task<string?> CreateAsync(string whatsapp, IReadOnlyDictionary<string, object?> data, CancellationToken cancellationToken);

    /// <summary>Regrava <c>last_pending:{whatsapp}</c> (<c>set_with_ttl</c> de 300 s do <c>/api/transacao</c>).</summary>
    Task SetLastPendingAsync(string whatsapp, string transactionId, CancellationToken cancellationToken);
}

/// <summary>Os 8 primeiros caracteres de um UUID v4 (<c>str(uuid.uuid4())[:8]</c>).</summary>
public interface IPendingIdGenerator
{
    string NewId();
}
