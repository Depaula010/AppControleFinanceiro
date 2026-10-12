using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Dashboard;

/// <summary>Janelas de data dos gráficos (<c>analytics_service.py:31-32, 54, 88</c>), sempre a partir de "hoje" de São Paulo.</summary>
public static class ChartsWindow
{
    public static DateOnly MonthStart(DateOnly today) => new(today.Year, today.Month, 1);

    public static DateOnly NextMonthStart(DateOnly today) => MonthStart(today).AddMonths(1);

    /// <summary>
    /// Desvio 090: dia 1 do mês de <c>hoje - (meses - 1)</c> meses, isto é, <c>meses</c> meses civis completos incluindo o
    /// atual (o Flask usava <c>dia 1 do mês - 30 * meses</c> dias, que deixava o primeiro mês parcial).
    /// </summary>
    public static DateOnly MonthlyStart(DateOnly today, int months) => MonthStart(today).AddMonths(-(months - 1));

    /// <summary><c>CURRENT_DATE - INTERVAL '90 days'</c>, com a data de São Paulo.</summary>
    public static DateOnly WeekdayStart(DateOnly today) => today.AddDays(-90);
}

/// <summary>Nome do dia da semana pelo número do <c>EXTRACT(DOW)</c> (0 = domingo), em inglês e sem preenchimento (desvio 009).</summary>
public static class WeekdayNames
{
    private static readonly string[] Names = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public static string For(int dayOfWeek) => Names[dayOfWeek];
}

/// <summary>Resultado de <see cref="MonthsParameter.Parse"/>.</summary>
public abstract record MonthsResult
{
    public sealed record Ok(int Months) : MonthsResult;

    public sealed record OutOfRange : MonthsResult;
}

/// <summary>
/// <c>request.args.get('meses', 3, type=int)</c> mais a faixa 1..12 (<c>api.py:208-215</c>, RN-006-08). Falha na
/// conversão devolve o padrão 3 sem erro; <c>int()</c> aceita espaços, sinal e <c>_</c> entre dígitos.
/// </summary>
public static partial class MonthsParameter
{
    public const int Default = 3;

    public const int Min = 1;

    public const int Max = 12;

    [GeneratedRegex(@"^\s*[+-]?[0-9]+(_[0-9]+)*\s*\z", RegexOptions.CultureInvariant)]
    private static partial Regex PythonInt();

    public static MonthsResult Parse(string? raw)
    {
        if (raw is null)
        {
            return new MonthsResult.Ok(Default);
        }

        if (!PythonInt().IsMatch(raw))
        {
            return new MonthsResult.Ok(Default);
        }

        var digits = raw.Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        var value = BigInteger.Parse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        return value < Min || value > Max ? new MonthsResult.OutOfRange() : new MonthsResult.Ok((int)value);
    }
}
