using MeuSecretario.Domain.Common;

namespace MeuSecretario.Domain.Bills;

/// <summary>Soma de <c>valor_previsto</c> por periodicidade e lado (receita ou despesa), já com <c>valor &gt; 0</c>.</summary>
public sealed record SummaryRow(string Periodicity, bool IsIncome, double Total);

public sealed record BillsSummary(
    double MonthlyExpenses,
    double AnnualExpenses,
    double MonthlyIncome,
    double ReserveGoal,
    double ReserveMonthlyEquivalent,
    int ReserveMonths);

/// <summary><c>GET /api/bills/summary</c> (spec 005 §3.5): fatores mensais do resumo e arredondamento do Python.</summary>
public static class BillsSummaryCalculator
{
    public const double WeeklyFactor = 4.33;
    public const double BiweeklyFactor = 2.16;
    public const int DailyFactor = 30;

    /// <param name="rows">Na ordem da consulta (<c>periodicidade, grupo</c>): a soma de <see cref="double"/> depende dela.</param>
    /// <param name="reserveTotal">Total da reserva pelo serviço único (desvio 014).</param>
    public static BillsSummary Calculate(IEnumerable<SummaryRow> rows, double reserveTotal, int months)
    {
        var expenses = 0.0;
        var annual = 0.0;
        var income = 0.0;

        foreach (var row in rows)
        {
            if (row.IsIncome)
            {
                // Receita anual não entra em nenhum total (Monthly devolve 0 para ANUAL).
                income += Monthly(row);
            }
            else if (row.Periodicity == Periodicities.Annual)
            {
                annual += row.Total;
            }
            else
            {
                expenses += Monthly(row);
            }
        }

        // Regra só do resumo: sem nada marcado para a reserva, usa as despesas mensais como base.
        if (reserveTotal == 0 && expenses > 0)
        {
            reserveTotal = expenses * months;
        }

        var equivalent = months > 0 ? reserveTotal / months : 0;
        return new BillsSummary(expenses, annual, income, PyRound.Round2(reserveTotal), PyRound.Round2(equivalent), months);
    }

    /// <summary>Contribuição mensal; <c>0</c> para <c>ANUAL</c> (separado) e periodicidade desconhecida.</summary>
    private static double Monthly(SummaryRow row) => row.Periodicity switch
    {
        Periodicities.Monthly => row.Total,
        Periodicities.Weekly => row.Total * WeeklyFactor,
        Periodicities.Biweekly => row.Total * BiweeklyFactor,
        Periodicities.Daily => row.Total * DailyFactor,
        _ => 0,
    };
}
