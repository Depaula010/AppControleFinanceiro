using MeuSecretario.Domain.Common;

namespace MeuSecretario.Domain.Dashboard;

/// <summary>Os cinco campos de <c>GET /api/dashboard/summary</c> (spec 006 §3.1).</summary>
public sealed record DashboardSummary(
    double TotalBalance, double MonthIncome, double MonthExpense, double MonthNet, string ReferenceMonth);

/// <summary><c>meses_pt[hoje.month]/hoje.year</c> (<c>api.py:146-153</c>), em português fixo e sem depender da cultura.</summary>
public static class DashboardMonths
{
    private static readonly string[] Names =
    [
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro",
    ];

    public static string Reference(DateOnly today) => $"{Names[today.Month - 1]}/{today.Year}";
}

/// <summary>Resumo do dashboard (RN-006-04 a RN-006-06; <c>api.py:105-163</c>).</summary>
public static class DashboardSummaryCalculator
{
    /// <param name="balances">Saldo de cada conta ativa (consolidadas).</param>
    /// <param name="incomeTotal">Soma de <c>Renda</c> do mês (negativa ou positiva; vira módulo).</param>
    /// <param name="expenseTotal">Soma de <c>Despesa</c> do mês.</param>
    public static DashboardSummary Calculate(
        IEnumerable<decimal> balances, decimal incomeTotal, decimal expenseTotal, DateOnly today)
    {
        // As parcelas têm 2 casas: a soma em decimal é exata e dá, depois do round(…, 2), o mesmo que o sum() de floats.
        var total = (double)balances.Sum();
        var income = (double)Math.Abs(incomeTotal);
        var expense = (double)Math.Abs(expenseTotal);
        var net = income - expense;

        return new DashboardSummary(
            PyRound.Round2(total),
            PyRound.Round2(income),
            PyRound.Round2(expense),
            PyRound.Round2(net),
            DashboardMonths.Reference(today));
    }
}
