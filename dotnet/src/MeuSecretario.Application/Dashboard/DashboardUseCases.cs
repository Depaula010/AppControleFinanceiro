using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Dashboard;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Application.Dashboard;

/// <summary><c>GET /api/dashboard/summary</c> e aliases (<c>api.py</c>).</summary>
public sealed class GetDashboardSummary(IDashboardRepository repository, IClock clock)
{
    public async Task<DashboardSummary> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var balances = await repository.ListActiveAccountBalancesAsync(userId, cancellationToken);
        var totals = await repository.SumByTypeAsync(userId, ChartsWindow.MonthStart(today), today, cancellationToken);

        // Só Renda e Despesa contam; Transferência e Pagamento Fatura são ignorados (api.py).
        var income = totals.Where(t => t.Type == TransactionTypes.Income).Sum(t => t.Total);
        var expense = totals.Where(t => t.Type == TransactionTypes.Expense).Sum(t => t.Total);

        return DashboardSummaryCalculator.Calculate(balances, income, expense, today);
    }
}

/// <summary><c>GET /api/dashboard/charts</c> (<c>api.py</c>; <c>analytics_service.py</c>). <c>meses</c> já validado.</summary>
public sealed class GetDashboardCharts(IDashboardRepository repository, IClock clock)
{
    public async Task<DashboardCharts> ExecuteAsync(int userId, int months, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var monthly = await repository.MonthlyExpensesAsync(userId, ChartsWindow.MonthlyStart(today, months), cancellationToken);
        var categories = await repository.CategoryExpensesAsync(
            userId, ChartsWindow.MonthStart(today), ChartsWindow.NextMonthStart(today), cancellationToken);
        var weekdays = await repository.WeekdayExpensesAsync(userId, ChartsWindow.WeekdayStart(today), cancellationToken);

        return new DashboardCharts(
            monthly.Select(m => new MonthlyChartItem(m.Month, Magnitude(m.Total))).ToList(),
            categories.Select(c => new CategoryChartItem(c.Macro, c.Subcategory, Magnitude(c.Total), c.Count)).ToList(),
            weekdays.Select(w => new WeekdayChartItem(WeekdayNames.For(w.DayOfWeek), Magnitude(w.Total), w.Count)).ToList());
    }

    private static double Magnitude(decimal total) => (double)Math.Abs(total);
}

/// <summary><c>GET /api/dashboard/recent</c> e aliases (<c>api.py</c>).</summary>
public sealed class GetRecentTransactions(IDashboardRepository repository)
{
    public Task<IReadOnlyList<RecentTransaction>> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        repository.ListRecentAsync(userId, cancellationToken);
}

/// <summary><c>GET /api/dashboard/alerts</c> (<c>api.py</c>).</summary>
public sealed class GetDashboardAlerts(IDashboardRepository repository, IClock clock)
{
    public async Task<IReadOnlyList<DashboardAlert>> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        // Os vencimentos caem entre hoje e hoje + 7: as transações pagas só interessam nos meses dessas duas datas.
        var today = clock.Today;
        var from = ChartsWindow.MonthStart(today);
        var toExclusive = ChartsWindow.NextMonthStart(today.AddDays(DashboardAlerts.WindowDays));
        return DashboardAlerts.Build(
            await repository.ListActiveSchedulesAsync(userId, from, toExclusive, cancellationToken), today);
    }
}
