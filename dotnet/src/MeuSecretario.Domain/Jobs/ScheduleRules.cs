using System.Globalization;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Domain.Jobs;

/// <summary>Tipos de <c>agendamentos.tipo_agendamento</c> (CHECK do banco).</summary>
public static class ScheduleKinds
{
    public const string Fixed = "FIXO";

    public const string Installment = "PARCELADO";

    public const string VariableReminder = "LEMBRETE_VARIAVEL";
}

/// <summary>Agendamento ativo com o que o motor, o populador e o check-in precisam (spec 013 §4.7).</summary>
/// <param name="GroupName">Nome do grupo da categoria; <c>Renda</c> vira transação de renda.</param>
/// <param name="AccountType">Tipo da conta (cartão de crédito vincula fatura).</param>
/// <param name="NextDue">Próximo vencimento de um agendamento de cadência contínua (<see cref="ScheduleCadence"/>); nulo nos demais.</param>
public sealed record ScheduleInfo(
    int Id,
    int UserId,
    int AccountId,
    int SubcategoryId,
    string Description,
    decimal? ExpectedAmount,
    string Kind,
    string Periodicity,
    int Day,
    int? Month,
    int? TotalInstallments,
    int ExecutedInstallments,
    int NotifyBeforeDays,
    string GroupName,
    string AccountType,
    string WhatsappNumber,
    DateOnly? NextDue = null);

/// <summary>Resultado de avançar uma parcela.</summary>
public readonly record struct InstallmentProgress(int Executed, bool Deactivate);

/// <summary>
/// Regras de execução de um agendamento (desvio 011: uma só definição, <c>&gt;=</c> para desativar; desvio 013: lembrete em
/// <c>dia_execucao − notificar_antes_dias</c>). Funções puras, sem relógio.
/// </summary>
public static class ScheduleRules
{
    public const string CardAccountType = "Cartão de Crédito";

    /// <summary><c>Renda</c> se o grupo da categoria é <c>Renda</c>, senão <c>Despesa</c> (R7).</summary>
    public static string TransactionType(string? groupName) =>
        groupName == "Renda" ? TransactionTypes.Income : TransactionTypes.Expense;

    /// <summary>Receita positiva, despesa negativa; <c>valor_previsto</c> nulo vale 0.</summary>
    public static decimal SignedAmount(string? groupName, decimal? expected)
    {
        var value = expected ?? 0m;
        return groupName == "Renda" ? value : -value;
    }

    /// <summary><c>f"{descricao} ({n}/{total})"</c> com <c>n = executadas + 1</c>; total nulo escreve <c>None</c> como o Python.</summary>
    public static string InstallmentDescription(string description, int executed, int? total) =>
        $"{description} ({(executed + 1).ToString(CultureInfo.InvariantCulture)}/{(total is { } t ? t.ToString(CultureInfo.InvariantCulture) : "None")})";

    /// <summary>Próxima parcela e se o agendamento deve ser desativado (<c>&gt;=</c>, desvio 011).</summary>
    public static InstallmentProgress NextProgress(int executed, int? total)
    {
        var next = executed + 1;
        return new InstallmentProgress(next, total is { } t && next >= t);
    }

    /// <summary>
    /// A definição única de "dá para lançar": ativo, e lembrete variável só quando o usuário confirma (<paramref name="allowVariableReminder"/>,
    /// desvio 252), e parcelado só com parcela restante. O SQL do check-in (<c>SchedulePaymentSql.Postable</c>) tem o mesmo critério.
    /// </summary>
    public static bool IsPostable(string kind, bool active, int executed, int? total, bool allowVariableReminder) =>
        active
        && (kind != ScheduleKinds.VariableReminder || allowVariableReminder)
        && (kind != ScheduleKinds.Installment || HasInstallmentLeft(executed, total));

    /// <summary>Uma parcela só é lançada se o total é nulo ou ainda há parcela a executar.</summary>
    public static bool HasInstallmentLeft(int executed, int? total) => total is null || executed < total;

    /// <summary>
    /// Data da transação do populador: <c>dia_execucao</c> no mês e ano do vencimento da fatura; dia inexistente cai no
    /// último dia do mês (<c>invoice_service.py</c>).
    /// </summary>
    public static DateOnly DateInMonth(DateOnly reference, int day) => MonthDays.Clamp(reference.Year, reference.Month, day);

    /// <summary>Filtro de ANUAL: só vale no mês de <c>mes_execucao</c>.</summary>
    public static bool MatchesMonth(string periodicity, int? scheduleMonth, int month) =>
        periodicity != "ANUAL" || scheduleMonth == month;

    /// <summary>
    /// O agendamento <c>FIXO</c>/<c>PARCELADO</c> é do dia de hoje? Igualdade com o dia do mês e, no <b>último dia do mês</b>,
    /// também os de <c>dia_execucao</c> maior (desvio 134: o Python só comparava <c>a.dia_execucao = :dia_hoje</c> e as contas
    /// de dia 29 a 31 ficavam sem lançamento em mês curto).
    /// </summary>
    public static bool MotorDayMatches(int scheduleDay, DateOnly today) =>
        scheduleDay == today.Day || (JobClock.IsLastDayOfMonth(today) && scheduleDay > today.Day);

    /// <summary>O agendamento é do dia de hoje para o motor? Cadência contínua: o próximo vencimento é hoje; senão, <see cref="MotorDayMatches(int, DateOnly)"/>.</summary>
    public static bool MotorDayMatches(ScheduleInfo schedule, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return ScheduleCadence.IsRolling(schedule.Periodicity) ? schedule.NextDue == today : MotorDayMatches(schedule.Day, today);
    }

    /// <summary>
    /// Data de vencimento a lembrar hoje, ou <c>null</c>. O lembrete cai em <c>vencimento − notificar_antes_dias</c> (desvio
    /// 013): o vencimento é <c>hoje + antes</c> e precisa ser o <c>dia_execucao</c> daquele mês (com o corte do último dia).
    /// </summary>
    public static DateOnly? ReminderDueDate(ScheduleInfo schedule, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var due = today.AddDays(Math.Max(0, schedule.NotifyBeforeDays));
        if (ScheduleCadence.IsRolling(schedule.Periodicity))
        {
            return schedule.NextDue == due ? due : null;
        }

        if (due.Day != Math.Min(schedule.Day, DateTime.DaysInMonth(due.Year, due.Month)))
        {
            return null;
        }

        return MatchesMonth(schedule.Periodicity, schedule.Month, due.Month) ? due : null;
    }

    /// <summary>
    /// Mensagem do lembrete (literal de <c>schedule_processor.py</c>). <c>valor_previsto</c> nulo ou zero vira
    /// <c>???</c> (o <c>if ag.valor_previsto</c> do Python).
    /// </summary>
    public static string ReminderMessage(ScheduleInfo schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var value = schedule.ExpectedAmount is { } amount && amount != 0m ? PtBrMoney.Format(amount) : "???";
        var when = ScheduleCadence.IsRolling(schedule.Periodicity) && schedule.NextDue is { } next
            ? next.ToString("dd/MM", CultureInfo.InvariantCulture)
            : schedule.Day.ToString(CultureInfo.InvariantCulture);
        return $"🔔 *LEMBRETE DE CONTA VARIÁVEL* 🔔\n\nSua conta '{schedule.Description}' vence em {schedule.NotifyBeforeDays.ToString(CultureInfo.InvariantCulture)} dias (no dia {when}).\n\nO valor previsto é: *{value}*\n\nPor favor, me diga o valor exato deste mês para eu registrar (ex: 'gastei 150.50 na conta de luz').";
    }
}
