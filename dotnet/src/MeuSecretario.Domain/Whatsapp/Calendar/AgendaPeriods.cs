namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Intervalo de datas de uma consulta de agenda ou de horários livres (as duas pontas incluídas).</summary>
public readonly record struct DateSpan(DateOnly Start, DateOnly End);

/// <summary>
/// Períodos da agenda (<c>CalendarQueryService.get_period_dates_for_calendar</c>) e dos horários livres
/// (<c>FreeTimeFinderService._calculate_date_range</c>), com o "hoje" informado (São Paulo).
/// </summary>
public static class AgendaPeriods
{
    /// <summary>Período desconhecido, <c>null</c> ou que não é texto vale <c>hoje</c>. Domingo em <c>esta_semana</c> dá um dia só (como o Flask).</summary>
    public static DateSpan ForAgenda(string? periodType, DateOnly today)
    {
        var weekday = Weekday(today);
        switch (periodType)
        {
            case "amanha":
                return new DateSpan(today.AddDays(1), today.AddDays(1));
            case "final_de_semana":
            {
                // dias_ate_sabado == 0 só ocorre no sábado; nos demais dias o resultado é o próximo sábado.
                var daysToSaturday = (((5 - weekday) % 7) + 7) % 7;
                var saturday = today.AddDays(daysToSaturday > 0 ? daysToSaturday : 0);
                return new DateSpan(saturday, saturday.AddDays(1));
            }

            case "esta_semana":
            {
                var daysToSunday = (((6 - weekday) % 7) + 7) % 7;
                return new DateSpan(today, daysToSunday > 0 ? today.AddDays(daysToSunday) : today);
            }

            case "proxima_semana":
            {
                var daysToMonday = (((7 - weekday) % 7) + 7) % 7;
                var monday = today.AddDays(daysToMonday > 0 ? daysToMonday : 7);
                return new DateSpan(monday, monday.AddDays(6));
            }

            case "este_mes":
                return Month(new DateOnly(today.Year, today.Month, 1));
            case "mes_passado":
                return Month(new DateOnly(today.Year, today.Month, 1).AddMonths(-1));
            case "proximo_mes":
                return Month(new DateOnly(today.Year, today.Month, 1).AddMonths(1));
            default:
                return new DateSpan(today, today);
        }
    }

    /// <summary><c>hoje</c>, <c>amanha</c>, <c>esta_semana</c> e <c>proxima_semana</c>; qualquer outro valor é inválido (<c>null</c>).</summary>
    public static DateSpan? ForFreeTime(string? periodType, DateOnly today)
    {
        var weekday = Weekday(today);
        switch (periodType)
        {
            case "hoje":
                return new DateSpan(today, today);
            case "amanha":
                return new DateSpan(today.AddDays(1), today.AddDays(1));
            case "esta_semana":
                return new DateSpan(today, today.AddDays(6 - weekday));
            case "proxima_semana":
            {
                var daysToMonday = (((7 - weekday) % 7) + 7) % 7;
                if (daysToMonday == 0)
                {
                    daysToMonday = 7;
                }

                var start = today.AddDays(daysToMonday);
                return new DateSpan(start, start.AddDays(6));
            }

            default:
                return null;
        }
    }

    /// <summary><c>date.weekday()</c> do Python: segunda = 0.</summary>
    public static int Weekday(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    private static DateSpan Month(DateOnly first) => new(first, first.AddMonths(1).AddDays(-1));
}
