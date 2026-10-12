using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Transactions;

// Porta pública de lançamento de transações (spec 004 §7.1), consumida pelas features 011, 012 e 013.
// Divergência da spec: TransactionKind mora em MeuSecretario.Domain.Transactions (as regras de entrada do Domain o
// produzem e o Domain não pode referenciar a Application).

public interface ITransactionPosting
{
    /// <summary>Receita ou despesa simples (create_transaction + vínculo de fatura). Uma transação do banco.</summary>
    Task<PostingResult<PostedTransaction>> PostSimpleAsync(PostSimpleTransaction command, CancellationToken cancellationToken);

    /// <summary>Par Transferência (create_transfer_pair).</summary>
    Task<PostingResult<PostedTransfer>> PostTransferAsync(PostTransfer command, CancellationToken cancellationToken);

    /// <summary>Par Pagamento Fatura + fatura Paga (create_fatura_payment, desvio 010).</summary>
    Task<PostingResult<PostedInvoicePayment>> PostInvoicePaymentAsync(PostInvoicePayment command, CancellationToken cancellationToken);

    /// <summary>1ª parcela como transação + agendamento PARCELADO das demais (create_transaction + create_parcelamento_agendamento).</summary>
    Task<PostingResult<PostedInstallmentPurchase>> PostInstallmentPurchaseAsync(PostInstallmentPurchase command, CancellationToken cancellationToken);
}

public sealed record PostSimpleTransaction(
    int UserId, int AccountId, int SubcategoryId, TransactionKind Kind,
    decimal Amount,                 // positivo ou zero, SEM sinal; o serviço aplica o sinal (R1)
    string Description, DateOnly Date,
    string? Notes = null, bool Consolidated = true,
    string? Origin = null,          // até 20 caracteres; null = NULL no banco
    int? ScheduleId = null);        // agendamento_id (motor de agendamentos, 013)

public sealed record PostTransfer(int UserId, int FromAccountId, int ToAccountId, decimal Amount, DateOnly Date, string? Origin = null);

public sealed record PostInvoicePayment(int UserId, int FromAccountId, int CardAccountId, decimal Amount, DateOnly Date, string? Origin = null);

public sealed record PostInstallmentPurchase(
    int UserId, int AccountId, int SubcategoryId,
    decimal InstallmentAmount,      // valor de CADA parcela, positivo; quem chama divide o total
    int TotalInstallments,          // >= 2
    string Description, DateOnly Date, string? Origin = null);

public sealed record PostedTransaction(int TransactionId, decimal StoredAmount, int? InvoiceId, DateOnly? InvoiceDueDate);

public sealed record PostedTransfer(int OutgoingTransactionId, int IncomingTransactionId, string FromAccountName, string ToAccountName);

public sealed record PostedInvoicePayment(
    int OutgoingTransactionId, int IncomingTransactionId, string FromAccountName, string CardName,
    int? InvoiceId, DateOnly? InvoiceDueDate, bool InvoiceMarkedPaid);

public sealed record PostedInstallmentPurchase(int TransactionId, int ScheduleId, int? InvoiceId);

public abstract record PostingResult<T>
{
    public sealed record Ok(T Value) : PostingResult<T>;

    public sealed record Failed(PostingError Error, string? Detail = null) : PostingResult<T>;
}

public enum PostingError
{
    AmountInvalid,                 // Amount < 0 (nunca nulo)
    AccountNotFound,               // conta (origem, no caso de transferência/pagamento) inexistente, de outro usuário ou inativa (desvio 039)
    DestinationAccountNotFound,    // conta destino / cartão inexistente, de outro usuário ou inativa
    SubcategoryNotFound,           // só PostSimple e PostInstallmentPurchase
    SameAccount,                   // origem == destino (transferência) ou origem == cartão (pagamento)
    NotACreditCard,                // PostInvoicePayment: a conta do cartão não é 'Cartão de Crédito' (desvio 071)
    InstallmentsInvalid,           // TotalInstallments < 2
    RequiredCategoryMissing,       // 'Investimentos de Curto Prazo' / 'Quitação de Empréstimos (Principal)' ausentes; Detail = nome (só para log)
}

public interface IInvoiceService
{
    /// <summary>ensure_current_invoice_exists, só a criação (§7.6). <paramref name="cardAccountId"/> nulo = todos os cartões ativos do usuário.</summary>
    Task EnsureCurrentAsync(int userId, int? cardAccountId, CancellationToken cancellationToken);
}

/// <summary>Gancho de 013/011: lança na fatura nova os agendamentos ativos do cartão (process_invoice_schedules).</summary>
public interface IInvoiceSchedulePopulator
{
    Task PopulateAsync(int invoiceId, int cardAccountId, DateOnly dueDate, CancellationToken cancellationToken);
}

/// <summary>Populador vazio: a fatura nova nasce vazia até a 013 registrar o real (Q-004-6).</summary>
public sealed class NullInvoiceSchedulePopulator : IInvoiceSchedulePopulator
{
    public Task PopulateAsync(int invoiceId, int cardAccountId, DateOnly dueDate, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>Diagnóstico sem PII do serviço de lançamento (a Application não conhece <c>ILogger</c>; a Api implementa).</summary>
public interface ITransactionsDiagnostics
{
    /// <summary>O populador falhou depois do commit; só o tipo da exceção (desvio 008).</summary>
    void PopulateFailed(int invoiceId, string errorType);
}
