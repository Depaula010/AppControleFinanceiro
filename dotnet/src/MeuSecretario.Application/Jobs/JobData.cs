using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Application.Jobs;

public sealed record NotifiedUser(int UserId, string WhatsappNumber);

public sealed record BillsReminderUser(int UserId, string WhatsappNumber, string Name);

public sealed record UserNotificationConfig(bool MorningSummaryActive, bool FinancialAlertsActive);

/// <summary><c>notificationconfigs</c> para os jobs (spec 013 RN-013-20, 33 e 41).</summary>
public interface IJobNotificationConfigRepository
{
    /// <summary>Resumo ou alertas ativos com <c>resumo_matinal_hora</c> igual a <paramref name="hour"/> (igualdade, sem janela).</summary>
    Task<IReadOnlyList<NotifiedUser>> ListMorningUsersAsync(TimeOnly hour, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotifiedUser>> ListCheckinUsersAsync(TimeOnly hour, CancellationToken cancellationToken);

    /// <summary>Alertas financeiros ativos com <c>resumo_matinal_hora BETWEEN min AND max</c>.</summary>
    Task<IReadOnlyList<BillsReminderUser>> ListBillsReminderUsersAsync(TimeOnly min, TimeOnly max, CancellationToken cancellationToken);

    /// <summary><c>INSERT ... ON CONFLICT DO NOTHING</c> com os padrões do banco e leitura.</summary>
    Task<UserNotificationConfig> GetOrCreateAsync(int userId, CancellationToken cancellationToken);
}

public interface IFinancialAlertsRepository
{
    /// <summary><c>get_upcoming_bills_and_invoices</c> (RN-013-30 e 31).</summary>
    Task<FinancialAlerts> GetUpcomingAsync(int userId, DateOnly day, CancellationToken cancellationToken);
}

public sealed record BriefingUser(string? City, string? State, string? WhatsappNumber);

public interface IBriefingDataRepository
{
    Task<BriefingUser> GetLocationAsync(int userId, CancellationToken cancellationToken);
}

public interface IBillsReminderRepository
{
    /// <summary>Contas do dia <paramref name="target"/> ainda sem lançamento no mês de <paramref name="today"/> (RN-013-33).</summary>
    Task<IReadOnlyList<ReminderBill>> ListAsync(int userId, DateOnly target, DateOnly today, CancellationToken cancellationToken);
}

public interface ICheckinQueries
{
    Task<CheckinSnapshot> GetSnapshotAsync(int userId, DateOnly today, CancellationToken cancellationToken);
}

public sealed record OpenInvoiceRef(int Id, int AccountId, DateOnly DueDate, string AccountName);

public interface IInvoiceJobRepository
{
    /// <summary>Fecha as <c>Aberta</c> com <c>data_fechamento &lt; today</c> e devolve as fechadas (RN-013-50).</summary>
    Task<IReadOnlyList<ClosedInvoice>> CloseExpiredAsync(DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<OpenInvoiceRef>> ListOpenAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DueSoonInvoice>> ListDueSoonAsync(DateOnly dueDate, int daysUntilDue, CancellationToken cancellationToken);
}

/// <param name="InvoiceId">Nulo = resolver a fatura do cartão na data.</param>
/// <param name="AllowVariableReminder">
/// Só a confirmação do usuário (check-in) lança <c>LEMBRETE_VARIAVEL</c>, pelo valor previsto; o motor automático nunca (padrão <c>false</c>).
/// </param>
/// <param name="Amount">Valor informado pelo usuário (lembrete variável); nulo = <c>valor_previsto</c> do agendamento.</param>
public sealed record ScheduleExecutionRequest(
    int ScheduleId, DateOnly Date, int? InvoiceId = null, bool AllowVariableReminder = false, decimal? Amount = null);

public abstract record ScheduleExecutionOutcome
{
    public sealed record Posted(int TransactionId, string Description, decimal Amount, int? InvoiceId) : ScheduleExecutionOutcome;

    public sealed record Skipped(string Reason) : ScheduleExecutionOutcome;

    /// <summary>Inativo, inexistente ou sem parcela a executar.</summary>
    public sealed record NotEligible : ScheduleExecutionOutcome;
}

public interface IScheduleExecutionRepository
{
    /// <summary>
    /// Candidatos do motor: <c>FIXO</c>/<c>PARCELADO</c> com <c>dia_execucao</c> = dia de hoje, ou maior no último dia do mês
    /// (desvio 134; ANUAL só no mês), e todos os
    /// <c>LEMBRETE_VARIAVEL</c> ativos (o Domain decide o lembrete).
    /// </summary>
    Task<IReadOnlyList<ScheduleInfo>> ListMotorCandidatesAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Uma transação: trava o agendamento, confere a idempotência (desvios 011 e 012), lança e avança a parcela.</summary>
    Task<ScheduleExecutionOutcome> ExecuteAsync(ScheduleExecutionRequest request, CancellationToken cancellationToken);
}

public sealed record ReportRecipient(int UserId, string Name, string WhatsappNumber, TimeOnly SendTime);

public interface IMonthlyReportRepository
{
    Task<IReadOnlyList<ReportRecipient>> ListRecipientsAsync(MonthlyMoment moment, TimeOnly min, TimeOnly max, CancellationToken cancellationToken);

    Task<ReportRecipient?> FindUserAsync(int userId, CancellationToken cancellationToken);

    Task<MonthlyReportData> GetDataAsync(int userId, MonthlyMoment moment, DateOnly today, CancellationToken cancellationToken);
}
