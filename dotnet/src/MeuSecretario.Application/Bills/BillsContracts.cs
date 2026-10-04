using MeuSecretario.Domain.Bills;

namespace MeuSecretario.Application.Bills;

/// <summary>Linha do GET (19 campos da spec 005 §3.1). <c>tipo_transacao</c> é derivado de <see cref="Group"/> e <see cref="MacroName"/>.</summary>
public sealed record BillListItem(
    int Id,
    string Description,
    decimal? Amount,
    string Type,
    string Periodicity,
    int Day,
    int? Month,
    int? NotifyDaysBefore,
    int SubcategoryId,
    string? SubcategoryName,
    string? MacroName,
    string? Group,
    int AccountId,
    string? AccountName,
    DateOnly StartDate,
    bool? IncludeInReserve,
    int? TotalInstallments,
    int? ExecutedInstallments);

/// <summary>Registro lido depois da gravação (POST e PUT): 16 campos da spec §3.2.</summary>
public sealed record BillDetails(
    int Id,
    string Description,
    decimal? Amount,
    string Type,
    string Periodicity,
    int Day,
    int? Month,
    int? NotifyDaysBefore,
    int SubcategoryId,
    string? SubcategoryName,
    int AccountId,
    string? AccountName,
    DateOnly StartDate,
    bool? IncludeInReserve,
    int? TotalInstallments,
    int? ExecutedInstallments);

/// <summary>Linha crua da reserva; o impacto depende dos meses do usuário e é calculado no serviço.</summary>
public sealed record ReserveItemRow(int ScheduleId, string Description, decimal Amount, string Periodicity, string? Category);

public abstract record CreateBillResult
{
    public sealed record Created(BillDetails Bill) : CreateBillResult;

    public sealed record Invalid(BillError Reason) : CreateBillResult;

    public sealed record AccountNotFound : CreateBillResult;

    public sealed record SubcategoryNotFound : CreateBillResult;
}

public abstract record UpdateBillResult
{
    public sealed record Updated(BillDetails Bill) : UpdateBillResult;

    public sealed record Invalid(BillError Reason) : UpdateBillResult;

    public sealed record NotFound : UpdateBillResult;

    public sealed record AccountNotFound : UpdateBillResult;

    public sealed record SubcategoryNotFound : UpdateBillResult;
}

/// <summary>Porta de persistência de agendamentos (spec 005). Todo método recebe o <c>userId</c> do token.</summary>
public interface IBillRepository
{
    /// <summary>Agendamentos <b>ativos</b> do usuário, ordenados por periodicidade, dia, descrição e id.</summary>
    Task<IReadOnlyList<BillListItem>> ListActiveAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Estado gravado do agendamento <b>ativo</b> do usuário, ou <c>null</c> (inexistente, inativo ou de outro usuário).</summary>
    Task<CurrentBill?> GetCurrentAsync(int userId, int billId, CancellationToken cancellationToken);

    Task<bool> ActiveAccountOwnedAsync(int userId, int accountId, CancellationToken cancellationToken);

    /// <summary>Subcategoria global ou do usuário (desvio 073).</summary>
    Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken cancellationToken);

    /// <summary>INSERT e releitura na mesma conexão.</summary>
    Task<BillDetails> CreateAsync(int userId, NewBillData data, CancellationToken cancellationToken);

    /// <summary>UPDATE só dos campos <c>IsSet</c> e releitura; <c>null</c> se o agendamento sumiu (404).</summary>
    Task<BillDetails?> UpdateAsync(int userId, int billId, BillChanges changes, CancellationToken cancellationToken);

    /// <summary>Remoção lógica (<c>ativo = false</c>); <c>false</c> se não há agendamento ativo do usuário.</summary>
    Task<bool> DeactivateAsync(int userId, int billId, CancellationToken cancellationToken);

    /// <summary>Totais por periodicidade e lado, já com <c>valor &gt; 0</c>, na ordem fixa da consulta.</summary>
    Task<IReadOnlyList<SummaryRow>> ListSummaryRowsAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Totais da reserva por periodicidade (ativos, marcados, FIXO/LEMBRETE_VARIAVEL, sem receita), em ordem fixa.</summary>
    Task<IReadOnlyList<PeriodTotal>> ListReserveTotalsAsync(int userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReserveItemRow>> ListReserveItemsAsync(int userId, CancellationToken cancellationToken);

    /// <summary><c>COALESCE(meses_reserva_emergencia, 6)</c>; usuário sem linha ⇒ 6.</summary>
    Task<int> GetReserveMonthsAsync(int userId, CancellationToken cancellationToken);
}
