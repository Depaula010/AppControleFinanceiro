using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Domain.Dashboard;

/// <summary>
/// Linha de <c>agendamentos</c> (ativa, que não seja de receita: desvio 095) que alimenta os alertas. Campos crus: o Flask
/// trata nulo e zero como ausente. <paramref name="PaidDates"/> traz as datas de transações já ligadas ao agendamento por
/// <c>agendamento_id</c> (ou, sem vínculo, pela mesma descrição) na janela dos alertas (desvio 094). Desvio 280: o repositório monta
/// <paramref name="PaidDates"/> com os <b>vencimentos</b> já pagos pela regra do check-in (competência, <c>SchedulePaymentSql</c>) e traz em
/// <paramref name="RollingNextDue"/> o próximo vencimento de quem tem cadência contínua (último lançamento + intervalo).
/// </summary>
public sealed record ScheduleRow(
    int Id,
    string Description,
    decimal? Amount,
    string Type,
    string? Periodicity,
    int? Day,
    int? Month,
    DateOnly? StartDate,
    string? SubcategoryName,
    IReadOnlyCollection<DateOnly>? PaidDates = null,
    DateOnly? RollingNextDue = null);

/// <summary>Um item de <c>GET /api/dashboard/alerts</c> (spec 006 §3.4).</summary>
public sealed record DashboardAlert(
    string Id,
    string Description,
    string? Subcategory,
    double? Amount,
    int? Day,
    int DaysLeft,
    DateOnly DueDate,
    string Level,
    string Type);

/// <summary>Contas agendadas que vencem nos próximos 7 dias (<c>api.py</c>, RN-006-10). Funções puras.</summary>
public static class DashboardAlerts
{
    public const int WindowDays = 7;

    /// <summary>Quantos dias para trás uma conta vencida e não paga ainda aparece como atrasada (desvio 280); além disso é do check-in.</summary>
    public const int OverdueDays = 7;

    public const string Danger = "danger";

    public const string Warning = "warning";

    public const string Info = "info";

    public static IReadOnlyList<DashboardAlert> Build(IEnumerable<ScheduleRow> rows, DateOnly today)
    {
        var end = today.AddDays(WindowDays);
        var alerts = new List<DashboardAlert>();

        // Ordem do SQL (dia_execucao, id); o OrderBy final é estável como o list.sort do Python.
        foreach (var row in rows.OrderBy(r => r.Day ?? 0).ThenBy(r => r.Id))
        {
            // Desvio 280: a conta que venceu nos últimos 7 dias e segue sem pagamento aparece como atrasada (dias_restantes negativo).
            var dues = new List<DateOnly>(2);
            if (Overdue(row, today) is { } late)
            {
                dues.Add(late);
            }

            if (NextDue(row, today) is { } due && due <= end)
            {
                dues.Add(due);
            }

            foreach (var d in dues.Where(d => !IsPaid(row, d)))
            {
                var daysLeft = d.DayNumber - today.DayNumber;
                alerts.Add(new DashboardAlert(
                    row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    row.Description,
                    row.SubcategoryName,
                    row.Amount is { } amount && amount != 0 ? (double)amount : null,
                    row.Day,
                    daysLeft,
                    d,
                    Level(daysLeft),
                    row.Type));
            }
        }

        return alerts.OrderBy(a => a.DaysLeft).ToList();
    }

    /// <summary>
    /// Desvio 094: a ocorrência já virou transação. <c>MENSAL</c> e <c>ANUAL</c> contam o mês do vencimento; as periodicidades
    /// curtas (semanal, quinzenal, diária) só contam a própria data do vencimento.
    /// </summary>
    public static bool IsPaid(ScheduleRow row, DateOnly due)
    {
        if (row.PaidDates is not { Count: > 0 } paid)
        {
            return false;
        }

        return row.Periodicity is "SEMANAL" or "QUINZENAL" or "DIARIA"
            ? paid.Contains(due)
            : paid.Any(d => d.Year == due.Year && d.Month == due.Month);
    }

    public static string Level(int daysLeft) => daysLeft switch
    {
        <= 0 => Danger,
        <= 2 => Warning,
        _ => Info,
    };

    /// <summary>
    /// Desvio 280: o vencimento mais recente nos últimos <see cref="OverdueDays"/> dias (antes de hoje), ou <c>null</c>. Quem decide se
    /// já foi pago é o <see cref="IsPaid"/>.
    /// </summary>
    public static DateOnly? Overdue(ScheduleRow row, DateOnly today)
    {
        var periodicity = string.IsNullOrEmpty(row.Periodicity) ? "MENSAL" : row.Periodicity;
        var day = row.Day ?? 0;
        var floor = today.AddDays(-OverdueDays);
        var candidates = new List<DateOnly>();

        switch (periodicity)
        {
            case "ANUAL":
                if (day >= 1 && row.Month is >= 1 and <= 12)
                {
                    candidates.Add(MonthDays.Clamp(today.Year - 1, row.Month.Value, day));
                    candidates.Add(MonthDays.Clamp(today.Year, row.Month.Value, day));
                }

                break;
            case "SEMANAL" or "QUINZENAL" or "DIARIA":
                if (row.RollingNextDue is { } rolling)
                {
                    candidates.Add(rolling);
                }

                break;
            default:
                if (day >= 1)
                {
                    var previous = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
                    candidates.Add(MonthDays.Clamp(previous.Year, previous.Month, day));
                    candidates.Add(MonthDays.Clamp(today.Year, today.Month, day));
                }

                break;
        }

        return candidates.Where(c => c < today && c >= floor && (row.StartDate is not { } s || c >= s)).Cast<DateOnly?>().LastOrDefault();
    }

    /// <summary>Próxima data de vencimento, ou <c>null</c> quando o Flask ignora o agendamento (<c>continue</c> ou <c>ValueError</c>).</summary>
    public static DateOnly? NextDue(ScheduleRow row, DateOnly today)
    {
        var periodicity = string.IsNullOrEmpty(row.Periodicity) ? "MENSAL" : row.Periodicity;
        var day = row.Day ?? 0;

        switch (periodicity)
        {
            case "ANUAL":
            {
                var month = row.Month ?? 0;
                if (day == 0 || month == 0 || day < 1 || month is < 1 or > 12)
                {
                    return null;
                }

                var due = MonthDays.Clamp(today.Year, month, day);
                return NotBeforeStart(due < today ? MonthDays.Clamp(today.Year + 1, month, day) : due, row.StartDate);
            }

            case "SEMANAL" or "QUINZENAL" or "DIARIA":
            {
                if (row.RollingNextDue is { } rolling)
                {
                    // Desvio 280: o próximo vencimento é o do banco (último lançamento + intervalo); atrasado é assunto do check-in.
                    return rolling >= today ? rolling : null;
                }

                if (row.StartDate is not { } start)
                {
                    return null;
                }

                var interval = periodicity switch { "SEMANAL" => 7, "QUINZENAL" => 14, _ => 1 };
                if (start >= today)
                {
                    return start;
                }

                var elapsed = today.DayNumber - start.DayNumber;
                var cycles = (elapsed + interval - 1) / interval;
                return start.AddDays(cycles * interval);
            }

            default:
            {
                if (day < 1)
                {
                    return null;
                }

                var thisMonth = MonthDays.Clamp(today.Year, today.Month, day);
                if (thisMonth >= today)
                {
                    return NotBeforeStart(thisMonth, row.StartDate);
                }

                var next = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
                return NotBeforeStart(MonthDays.Clamp(next.Year, next.Month, day), row.StartDate);
            }
        }
    }

    /// <summary>Desvio 093: vencimento anterior a <c>data_inicio</c> não gera alerta (o agendamento ainda não começou).</summary>
    private static DateOnly? NotBeforeStart(DateOnly due, DateOnly? start) => start is { } s && due < s ? null : due;
}
