namespace MeuSecretario.Domain.Jobs;

/// <summary>Aritmética de hora e de calendário dos jobs (spec 013 RN-013-06, RN-013-33, RN-013-70 e RN-013-71).</summary>
public static class JobClock
{
    /// <summary><c>datetime.now().time().replace(second=0, microsecond=0)</c>: só hora e minuto.</summary>
    public static TimeOnly HourMinute(DateTime now) => new(now.Hour, now.Minute);

    /// <summary>
    /// <c>(datetime.combine(hoje, t) ± timedelta(minutes=n)).time()</c>: hora do dia com volta de meia-noite. A janela
    /// pode ter <c>Min &gt; Max</c>, e então o <c>BETWEEN</c> do banco não casa nada, como no Python.
    /// </summary>
    public static (TimeOnly Min, TimeOnly Max) Window(TimeOnly time, int minutes) =>
        (time.AddMinutes(-minutes), time.AddMinutes(minutes));

    public static bool IsLastDayOfMonth(DateOnly date) => date.Day == DateTime.DaysInMonth(date.Year, date.Month);

    /// <summary>Texto <c>HH:mm</c> (<c>strftime('%H:%M')</c>).</summary>
    public static string Hhmm(TimeOnly time) => time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
