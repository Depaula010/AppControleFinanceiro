using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Application.Accounts;

public sealed record AccountDetails(
    int Id,
    string NomeConta,
    string TipoConta,
    decimal SaldoInicial,
    string? CorHex,
    string? Icone,
    decimal? LimiteCredito,
    int? DiaFechamento,
    int? DiaVencimento);

/// <summary>Linha do GET: os detalhes (desvio 038) mais o saldo atual (<c>saldo_inicial</c> + transações consolidadas, desvio 007).</summary>
public sealed record AccountListItem(
    int Id,
    string NomeConta,
    string TipoConta,
    decimal SaldoInicial,
    string? CorHex,
    string? Icone,
    decimal? LimiteCredito,
    int? DiaFechamento,
    int? DiaVencimento,
    decimal Saldo);

public abstract record CreateAccountResult
{
    public sealed record Created(AccountDetails Account) : CreateAccountResult;

    public sealed record Invalid(AccountError Reason) : CreateAccountResult;

    /// <summary>Já existe conta <b>ativa</b> com o nome (a inativa é reativada, desvio 032).</summary>
    public sealed record NameTaken : CreateAccountResult;
}

public abstract record UpdateAccountResult
{
    public sealed record Updated(AccountDetails Account) : UpdateAccountResult;

    public sealed record Invalid(AccountError Reason) : UpdateAccountResult;

    public sealed record NotFound : UpdateAccountResult;

    public sealed record NameTaken : UpdateAccountResult;
}

public abstract record DeleteAccountResult
{
    public sealed record Deleted : DeleteAccountResult;

    public sealed record NotFound : DeleteAccountResult;

    public sealed record HasTransactions(int Count) : DeleteAccountResult;

    public sealed record HasActiveSchedules(int Count) : DeleteAccountResult;
}

/// <summary>Porta de persistência de contas (spec 002). Todo método recebe o <c>userId</c> do token: nada vale sem ele.</summary>
public interface IAccountRepository
{
    Task<IReadOnlyList<AccountListItem>> ListActiveAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Conta <b>ativa</b> do usuário, ou <c>null</c> (inexistente, inativa ou de outro usuário).</summary>
    Task<AccountDetails?> GetActiveAsync(int userId, int accountId, CancellationToken cancellationToken);

    /// <summary>Se outra conta do usuário (ativa ou inativa, desvio 016) já tem o nome.</summary>
    Task<bool> NameTakenAsync(int userId, string name, int exceptAccountId, CancellationToken cancellationToken);

    /// <summary>Cria, ou reativa a conta inativa de mesmo nome (desvio 032). Só devolve <c>Created</c> ou <c>NameTaken</c>.</summary>
    Task<CreateAccountResult> CreateAsync(int userId, NewAccountData data, CancellationToken cancellationToken);

    /// <summary>Só devolve <c>Updated</c>, <c>NotFound</c> ou <c>NameTaken</c> (corrida na UNIQUE).</summary>
    Task<UpdateAccountResult> UpdateAsync(int userId, int accountId, AccountChanges changes, CancellationToken cancellationToken);

    /// <summary>Remoção lógica numa transação (RN-002-14, RN-002-15, desvio 037).</summary>
    Task<DeleteAccountResult> SoftDeleteAsync(int userId, int accountId, CancellationToken cancellationToken);
}
