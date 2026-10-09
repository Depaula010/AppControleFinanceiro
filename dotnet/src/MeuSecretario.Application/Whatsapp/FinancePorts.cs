using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

// Portas das sub-entregas 012a (transações), 012b (pagamentos) e 012c (consultas financeiras). Cada leitura é uma consulta
// pequena; Infrastructure implementa com Dapper (colunas minúsculas, texto do cliente sempre parâmetro).

/// <summary>Agendamento recorrente achado pela descrição (<c>get_lembrete_variavel_by_description</c>).</summary>
public sealed record RecurringSchedule(string Description, decimal? ExpectedAmount, int AccountId, int SubcategoryId);

/// <summary><c>usuarios.conta_padrao_renda_id</c> e <c>conta_padrao_despesa_id</c>.</summary>
public sealed record DefaultAccounts(int? IncomeAccountId, int? ExpenseAccountId);

/// <summary>Leituras de contas e agendamentos usadas pela escolha de conta e pelas intenções de transação.</summary>
public interface IWhatsAppFinanceReader
{
    /// <summary>Conta <b>ativa</b> do usuário com esse id (desvio 039); <c>null</c> = não existe ou está inativa.</summary>
    Task<AccountRef?> FindActiveAccountAsync(int userId, int accountId, CancellationToken cancellationToken);

    Task<DefaultAccounts> GetDefaultAccountsAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>AgendamentosQueries.get_lembrete_variavel_by_description</c> (spec 012 §7.7): ativo, <c>FIXO</c>/<c>LEMBRETE_VARIAVEL</c>,
    /// <c>descricao ILIKE %x%</c>; igualdade exata primeiro, depois <c>LEMBRETE_VARIAVEL</c>, depois <c>descricao</c>; <c>LIMIT 1</c>.
    /// </summary>
    Task<RecurringSchedule?> FindRecurringByDescriptionAsync(int userId, string description, CancellationToken cancellationToken);

    /// <summary>
    /// <c>get_account_by_name</c>: nome exato, depois <c>ILIKE %nome%</c> (menor id), depois (com <paramref name="fallback"/>) a primeira
    /// conta do tipo ou de qualquer tipo. Só contas ativas (desvio 039).
    /// </summary>
    Task<int?> FindAccountIdByNameAsync(int userId, string name, bool fallback, string? type, CancellationToken cancellationToken);
}

/// <summary>Fatura do ciclo de uma data (<c>get_or_create_fatura</c>), comitada antes de seguir. <c>null</c> = não é cartão com os dois dias.</summary>
public interface IInvoiceForDate
{
    Task<int?> ResolveAsync(int userId, int accountId, DateOnly day, CancellationToken cancellationToken);
}

/// <summary>Leituras do feedback da transação salva (<c>transaction_feedback_service.py</c>).</summary>
public interface IFeedbackReader
{
    Task<FeedbackTransaction?> FindTransactionAsync(int transactionId, CancellationToken cancellationToken);

    /// <summary>Pote ativo da subcategoria, com o gasto do período do pote (desvio 015); <c>null</c> = nenhum pote.</summary>
    Task<PotFeedback?> FindPotAsync(int userId, int subcategoryId, DateOnly today, CancellationToken cancellationToken);

    /// <summary><c>null</c> = sem linha em <c>notificationconfigs</c> (mostra o pote).</summary>
    Task<PotAlertConfig?> GetPotAlertConfigAsync(int userId, CancellationToken cancellationToken);

    /// <summary><c>calcular_fatura_atual</c>: <c>null</c> se a conta não é <c>Cartão de Crédito</c> do usuário.</summary>
    Task<FeedbackFooter.Invoice?> GetInvoiceFooterAsync(int userId, int accountId, DateOnly today, CancellationToken cancellationToken);

    /// <summary><c>calcular_saldo_conta</c>; saldo inicial nulo no banco: a leitura lança (o <c>float()</c> do Python quebra).</summary>
    Task<FeedbackFooter.Balance?> GetBalanceFooterAsync(int userId, int accountId, CancellationToken cancellationToken);
}

/// <summary><c>alerta_potes_ativo</c> e <c>alerta_potes_threshold</c>.</summary>
public sealed record PotAlertConfig(bool Active, int? Threshold);

/// <summary>Mensagem de feedback depois de gravar (<c>gerar_feedback_transacao</c>); qualquer falha vira o texto simples.</summary>
public interface IFeedbackService
{
    Task<string> GenerateAsync(int transactionId, CancellationToken cancellationToken);
}

/// <summary>Diagnóstico sem PII das intenções financeiras (a Application não conhece <c>ILogger</c>; a Api implementa): só o tipo da exceção.</summary>
public interface IFinanceDiagnostics
{
    void FeedbackFailed(string errorType);

    void PaymentItemFailed(string errorType);
}

/// <summary>
/// Quita uma conta fixa (<c>FixedBillsService.settle_fixed_bill</c>): despesa na conta do agendamento, com a descrição do
/// agendamento. Devolve o id da transação.
/// </summary>
public interface IFixedBillSettler
{
    Task<int> SettleAsync(int userId, int scheduleId, double amount, DateOnly day, CancellationToken cancellationToken);
}
