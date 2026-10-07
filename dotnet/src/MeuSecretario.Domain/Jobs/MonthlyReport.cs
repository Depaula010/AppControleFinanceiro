using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Jobs;

public enum MonthlyMoment
{
    InicioMes,
    FimMes,
}

public sealed record ReportTotals(double TotalExpenses, double TotalIncome, double PeriodBalance, int TotalTransactions);

public sealed record ReportCategory(string Category, double Value, double Percent);

public sealed record ReportComparison(double CurrentMonth, double PreviousMonth, double VariationValue, double VariationPercent);

public sealed record ReportPot(string Name, double Limit, double Used, double Balance, double Percent);

public sealed record ReportAccounts(int Paid, double PaidValue, int Pending, double PendingValue);

/// <summary><c>generate_monthly_report_data</c> (RN-013-73). Valores em <see cref="double"/> como o <c>float</c> do Python.</summary>
public sealed record MonthlyReportData(
    int Month,
    int Year,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    ReportTotals Totals,
    IReadOnlyList<ReportCategory> TopCategories,
    ReportComparison Comparison,
    IReadOnlyList<ReportPot> Pots,
    ReportAccounts Accounts);

/// <summary>Período e cálculos do relatório mensal, com o comportamento do Flask (inclusive o sinal; desvio proposto 132).</summary>
public static class MonthlyReportRules
{
    public static readonly string[] MonthNames =
        ["", "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"];

    /// <summary><c>INICIO_MES</c> relata o mês anterior; <c>FIM_MES</c>, o corrente (<c>calcular_periodo_relatorio</c>).</summary>
    public static (int Month, int Year, DateOnly Start, DateOnly End) Period(MonthlyMoment moment, DateOnly today)
    {
        int month;
        int year;
        if (moment == MonthlyMoment.InicioMes)
        {
            if (today.Month == 1)
            {
                (month, year) = (12, today.Year - 1);
            }
            else
            {
                (month, year) = (today.Month - 1, today.Year);
            }
        }
        else
        {
            (month, year) = (today.Month, today.Year);
        }

        return (month, year, new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)));
    }

    /// <summary>Mês anterior ao início do período (<c>get_comparacao_mes_anterior</c>).</summary>
    public static (DateOnly Start, DateOnly End) PreviousMonth(DateOnly periodStart)
    {
        var previous = periodStart.AddMonths(-1);
        return (new DateOnly(previous.Year, previous.Month, 1), new DateOnly(previous.Year, previous.Month, DateTime.DaysInMonth(previous.Year, previous.Month)));
    }

    public static string MomentName(MonthlyMoment moment) => moment == MonthlyMoment.InicioMes ? "INICIO_MES" : "FIM_MES";

    public static ReportTotals Totals(double expenses, double incomes, int transactions) =>
        new(expenses, incomes, incomes - expenses, transactions);

    public static IReadOnlyList<ReportCategory> TopCategories(IReadOnlyList<(string Category, double Value)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var total = rows.Sum(r => r.Value);
        return [.. rows.Select(r => new ReportCategory(r.Category, r.Value, total != 0 ? r.Value / total * 100 : 0))];
    }

    public static ReportComparison Comparison(double current, double previous)
    {
        var variation = current - previous;
        return new ReportComparison(current, previous, variation, previous > 0 ? variation / previous * 100 : 0);
    }

    public static ReportPot Pot(string name, double limit, double used) =>
        new(name, limit, used, limit - used, limit > 0 ? used / limit * 100 : 0);

    public static string Caption(MonthlyReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return $"📊 Relatório de {MonthNames[data.Month]}/{data.Year.ToString(CultureInfo.InvariantCulture)}";
    }
}

/// <summary><c>format_report_message</c> (<c>monthly_report_service.py:374-441</c>): texto literal, números no formato Python (desvio proposto 133).</summary>
public static class MonthlyReportFormatter
{
    public static string Format(MonthlyReportData data, string userName)
    {
        ArgumentNullException.ThrowIfNull(data);
        var totals = data.Totals;
        var comparison = data.Comparison;
        var message = new StringBuilder();

        message.Append(CultureInfo.InvariantCulture, $"📊 *RELATÓRIO MENSAL - {MonthlyReportRules.MonthNames[data.Month].ToUpperInvariant()}/{data.Year}*\n");
        message.Append(CultureInfo.InvariantCulture, $"Olá, {userName}!\n\n");

        message.Append("💰 *RESUMO FINANCEIRO*\n");
        message.Append(CultureInfo.InvariantCulture, $"• Receitas: R$ {PyNumber.Thousands(totals.TotalIncome)}\n");
        message.Append(CultureInfo.InvariantCulture, $"• Despesas: R$ {PyNumber.Thousands(totals.TotalExpenses)}\n");
        message.Append(CultureInfo.InvariantCulture, $"• Saldo: R$ {PyNumber.Thousands(totals.PeriodBalance)}\n");
        message.Append(CultureInfo.InvariantCulture, $"• Transações: {totals.TotalTransactions}\n\n");

        var symbol = comparison.VariationValue > 0 ? "📈" : "📉";
        message.Append(CultureInfo.InvariantCulture, $"{symbol} *COMPARAÇÃO COM MÊS ANTERIOR*\n");
        message.Append(CultureInfo.InvariantCulture, $"• Mês anterior: R$ {PyNumber.Thousands(comparison.PreviousMonth)}\n");
        message.Append(CultureInfo.InvariantCulture, $"• Variação: R$ {PyNumber.Thousands(comparison.VariationValue)} ");
        message.Append(CultureInfo.InvariantCulture, $"({PyNumber.WithSign(comparison.VariationPercent)}%)\n\n");

        if (data.TopCategories.Count > 0)
        {
            message.Append("🏆 *TOP 5 CATEGORIAS*\n");
            var index = 1;
            foreach (var category in data.TopCategories)
            {
                message.Append(CultureInfo.InvariantCulture,
                    $"{index}. {category.Category}: R$ {PyNumber.Thousands(category.Value)} ({PyNumber.Fixed(category.Percent, 1)}%)\n");
                index++;
            }

            message.Append('\n');
        }

        if (data.Pots.Count > 0)
        {
            message.Append("🎯 *POTES DE GASTOS*\n");
            foreach (var pot in data.Pots)
            {
                var emoji = pot.Percent <= 100 ? "✅" : "⚠️";
                message.Append(CultureInfo.InvariantCulture, $"{emoji} {pot.Name}:\n");
                message.Append(CultureInfo.InvariantCulture, $"   Usado: R$ {PyNumber.Thousands(pot.Used)} / R$ {PyNumber.Thousands(pot.Limit)}\n");
                message.Append(CultureInfo.InvariantCulture, $"   Saldo: R$ {PyNumber.Thousands(pot.Balance)} ({PyNumber.Fixed(pot.Percent, 1)}%)\n");
            }

            message.Append('\n');
        }

        message.Append("💳 *STATUS DE CONTAS*\n");
        message.Append(CultureInfo.InvariantCulture, $"✅ Pagas: {data.Accounts.Paid} (R$ {PyNumber.Thousands(data.Accounts.PaidValue)})\n");
        message.Append(CultureInfo.InvariantCulture, $"⏳ Pendentes: {data.Accounts.Pending} (R$ {PyNumber.Thousands(data.Accounts.PendingValue)})\n\n");
        message.Append("_📷 Veja o gráfico de pizza anexo para visualização das categorias!_");
        return message.ToString();
    }
}
