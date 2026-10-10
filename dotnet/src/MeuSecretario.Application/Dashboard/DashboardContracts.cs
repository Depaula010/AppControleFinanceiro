using MeuSecretario.Domain.Dashboard;

namespace MeuSecretario.Application.Dashboard;

/// <summary>Soma de <c>valor</c> por <c>tipo_transacao</c> (resumo do mês).</summary>
public sealed record TypeTotal(string Type, decimal Total);

/// <summary>Despesa de um mês <c>YYYY-MM</c> (soma negativa, como gravada).</summary>
public sealed record MonthlyExpense(string Month, decimal Total);

/// <summary>Despesa de uma subcategoria no mês (soma negativa e contagem).</summary>
public sealed record CategoryExpense(string Macro, string Subcategory, decimal Total, int Count);

/// <summary>Despesa de um dia da semana (0 = domingo; soma negativa e contagem).</summary>
public sealed record WeekdayExpense(int DayOfWeek, decimal Total, int Count);

/// <summary>Uma das 10 últimas transações (<c>tipo</c> é o valor do banco, spec 006 §3.3).</summary>
public sealed record RecentTransaction(
    int Id, string Description, decimal Amount, string DbType, DateOnly Date, string Category, string AccountName);

public sealed record MonthlyChartItem(string Month, double Total);

public sealed record CategoryChartItem(string Macro, string Subcategory, double Total, int Count);

public sealed record WeekdayChartItem(string Day, double Total, int Count);

public sealed record DashboardCharts(
    IReadOnlyList<MonthlyChartItem> Monthly,
    IReadOnlyList<CategoryChartItem> Categories,
    IReadOnlyList<WeekdayChartItem> Weekdays);

/// <summary>Leituras do dashboard. Todo método recebe o <c>userId</c> do token; nada é escrito.</summary>
public interface IDashboardRepository
{
    /// <summary>Saldo (<c>saldo_inicial</c> + consolidadas) de cada conta ativa, <c>ORDER BY tipo_conta, nome_conta, id</c>.</summary>
    Task<IReadOnlyList<decimal>> ListActiveAccountBalancesAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Soma por tipo das transações consolidadas com data entre <paramref name="firstDay"/> e <paramref name="lastDay"/>, inclusive.</summary>
    Task<IReadOnlyList<TypeTotal>> SumByTypeAsync(int userId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken);

    /// <summary>Despesas consolidadas desde <paramref name="from"/>, por mês, do mais antigo para o mais novo (desvio 091).</summary>
    Task<IReadOnlyList<MonthlyExpense>> MonthlyExpensesAsync(int userId, DateOnly from, CancellationToken cancellationToken);

    /// <summary>Despesas consolidadas no intervalo semiaberto, por macro e subcategoria, <c>ORDER BY total ASC LIMIT 10</c> (soma negativa: maior gasto primeiro, desvio 092).</summary>
    Task<IReadOnlyList<CategoryExpense>> CategoryExpensesAsync(
        int userId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken);

    /// <summary>Despesas consolidadas desde <paramref name="from"/>, por dia da semana, de domingo a sábado.</summary>
    Task<IReadOnlyList<WeekdayExpense>> WeekdayExpensesAsync(int userId, DateOnly from, CancellationToken cancellationToken);

    /// <summary>As 10 últimas transações (data e <c>created_at</c> decrescentes), de qualquer tipo.</summary>
    Task<IReadOnlyList<RecentTransaction>> ListRecentAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Agendamentos de despesa com <c>ativo = true</c> (receita fica fora, desvio 095), <c>ORDER BY dia_execucao, id</c>, cada um
    /// com as datas de transação já ligadas a ele no intervalo semiaberto <paramref name="paidFrom"/>..<paramref name="paidToExclusive"/> (desvio 094).
    /// </summary>
    Task<IReadOnlyList<ScheduleRow>> ListActiveSchedulesAsync(
        int userId, DateOnly paidFrom, DateOnly paidToExclusive, CancellationToken cancellationToken);
}
