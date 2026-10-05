using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Domain.Dashboard;

/// <summary>Linha de <c>agendamentos</c> (ativa) que alimenta os alertas. Campos crus: o Flask trata nulo e zero como ausente.</summary>
public sealed record ScheduleRow(
    int Id,
    string Description,
    decimal? Amount,
    string Type,
    string? Periodicity,
    int? Day,
    int? Month,
    DateOnly? StartDate,
    string? SubcategoryName);

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

/// <summary>Contas agendadas que vencem nos próximos 7 dias (<c>api.py:1198-1288</c>, RN-006-10). Funções puras.</summary>
public static class DashboardAlerts
{
    public const int WindowDays = 7;

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
            if (NextDue(row, today) is not { } due || due > end)
            {
                continue;
            }

            var daysLeft = due.DayNumber - today.DayNumber;
            alerts.Add(new DashboardAlert(
                row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.Description,
                row.SubcategoryName,
                row.Amount is { } amount && amount != 0 ? (double)amount : null,
                row.Day,
                daysLeft,
                due,
                Level(daysLeft),
                row.Type));
        }

        return alerts.OrderBy(a => a.DaysLeft).ToList();
    }

    public static string Level(int daysLeft) => daysLeft switch
    {
        0 => Danger,
        <= 2 => Warning,
        _ => Info,
    };

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
                return due < today ? MonthDays.Clamp(today.Year + 1, month, day) : due;
            }

            case "SEMANAL" or "QUINZENAL" or "DIARIA":
            {
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
                    return thisMonth;
                }

                var next = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
                return MonthDays.Clamp(next.Year, next.Month, day);
            }
        }
    }
}
