namespace MeuSecretario.Domain.Bills;

/// <summary>Soma de <c>valor_previsto</c> de uma periodicidade (já filtrada pelo repositório).</summary>
public sealed record PeriodTotal(string Periodicity, double Total);

/// <summary>Fator de cada periodicidade na reserva de emergência (RN-005-11).</summary>
public static class ReserveFactor
{
    public const double WeeksPerMonth = 4.33;
    public const int DaysPerMonth = 30;

    /// <summary>
    /// <c>MENSAL N</c>, <c>ANUAL 1</c> (integral), <c>SEMANAL round(N × 4,33)</c> (meio-par, como o <c>round</c> do Python),
    /// <c>QUINZENAL 2N</c>, <c>DIARIA 30N</c>; periodicidade desconhecida não entra (0).
    /// </summary>
    public static double For(string periodicity, int months) => periodicity switch
    {
        Periodicities.Monthly => months,
        Periodicities.Annual => 1,
        Periodicities.Weekly => Math.Round(months * WeeksPerMonth, MidpointRounding.ToEven),
        Periodicities.Biweekly => months * 2,
        Periodicities.Daily => months * DaysPerMonth,
        _ => 0,
    };
}

public sealed record ReserveResult(double Total, double MonthlyEquivalent);

/// <summary>Cálculo único da reserva de emergência (desvio 014): sem arredondar.</summary>
public static class EmergencyReserve
{
    /// <summary>Soma na ordem recebida (a consulta fixa <c>ORDER BY periodicidade</c>, RN-005-13).</summary>
    public static ReserveResult Calculate(int months, IEnumerable<PeriodTotal> totals)
    {
        var total = 0.0;
        foreach (var period in totals)
        {
            total += period.Total * ReserveFactor.For(period.Periodicity, months);
        }

        return new ReserveResult(total, months > 0 ? total / months : 0);
    }

    /// <summary>Peso de uma linha na reserva: valor × fator da periodicidade.</summary>
    public static double Impact(double amount, string periodicity, int months) =>
        amount * ReserveFactor.For(periodicity, months);
}
