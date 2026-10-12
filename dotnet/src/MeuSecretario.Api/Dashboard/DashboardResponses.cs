using System.Globalization;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Application.Dashboard;
using MeuSecretario.Domain.Dashboard;

namespace MeuSecretario.Api.Dashboard;

/// <summary>Corpos de resposta do dashboard (envelope A). As chaves saem na ordem da spec 006 §3.</summary>
public static class DashboardResponses
{
    public static IResult Summary(DashboardSummary summary) =>
        Envelopes.A.Success(new Dictionary<string, object?>
        {
            ["saldo_total"] = summary.TotalBalance,
            ["receitas_mes"] = summary.MonthIncome,
            ["despesas_mes"] = summary.MonthExpense,
            ["saldo_mes"] = summary.MonthNet,
            ["mes_referencia"] = summary.ReferenceMonth,
        });

    public static IResult Charts(DashboardCharts charts) =>
        Envelopes.A.Success(new Dictionary<string, object?>
        {
            ["gastos_mensais"] = charts.Monthly.Select(m => new Dictionary<string, object?>
            {
                ["mes"] = m.Month,
                ["total"] = m.Total,
            }).ToList(),
            ["gastos_categoria"] = charts.Categories.Select(c => new Dictionary<string, object?>
            {
                ["macro_categoria"] = c.Macro,
                ["subcategoria"] = c.Subcategory,
                ["total"] = c.Total,
                ["quantidade"] = c.Count,
            }).ToList(),
            ["gastos_dia_semana"] = charts.Weekdays.Select(w => new Dictionary<string, object?>
            {
                ["dia"] = w.Day,
                ["total"] = w.Total,
                ["quantidade"] = w.Count,
            }).ToList(),
        });

    /// <summary><c>tipo</c> é o valor do banco, sem converter <c>Renda</c> em <c>Receita</c> (desvio 089 proposto).</summary>
    public static IResult Recent(IReadOnlyList<RecentTransaction> transactions) =>
        Envelopes.A.Success(transactions.Select(t => new Dictionary<string, object?>
        {
            ["id"] = t.Id,
            ["descricao"] = t.Description,
            ["valor"] = (double)t.Amount,
            ["tipo"] = t.DbType,
            ["data"] = Date(t.Date),
            ["categoria"] = t.Category,
            ["conta"] = t.AccountName,
        }).ToList());

    public static IResult Alerts(IReadOnlyList<DashboardAlert> alerts) =>
        Envelopes.A.Success(alerts.Select(a => new Dictionary<string, object?>
        {
            ["id"] = a.Id,
            ["descricao"] = a.Description,
            ["subcategoria"] = a.Subcategory,
            ["valor_previsto"] = a.Amount,
            ["dia_execucao"] = a.Day,
            ["dias_restantes"] = a.DaysLeft,
            ["data_vencimento"] = Date(a.DueDate),
            ["tipo"] = a.Level,
            ["tipo_agendamento"] = a.Type,
        }).ToList());

    public static IResult Error(string message, int statusCode) => Envelopes.A.Error(message, statusCode);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
