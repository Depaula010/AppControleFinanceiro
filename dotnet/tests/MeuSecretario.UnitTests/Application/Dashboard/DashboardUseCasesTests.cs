using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Dashboard;
using MeuSecretario.Domain.Dashboard;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Dashboard;

public class DashboardUseCasesTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private readonly IDashboardRepository _repository = Substitute.For<IDashboardRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public DashboardUseCasesTests()
    {
        _clock.Today.Returns(Today);
    }

    [Fact]
    [Trait("AC", "006-14")]
    public async Task Summary_UsesTheMonthWindowUpToTodayAndIgnoresOtherTypes()
    {
        _repository.ListActiveAccountBalancesAsync(7, Arg.Any<CancellationToken>()).Returns([100m, 70m]);
        _repository.SumByTypeAsync(7, new DateOnly(2026, 10, 1), Today, Arg.Any<CancellationToken>()).Returns(
        [
            new TypeTotal("Renda", 1000m),
            new TypeTotal("Despesa", -300m),
            new TypeTotal("Transferência", -50m),
            new TypeTotal("Pagamento Fatura", -80m),
        ]);

        var summary = await new GetDashboardSummary(_repository, _clock).ExecuteAsync(7, CancellationToken.None);

        summary.ShouldBe(new DashboardSummary(170, 1000, 300, 700, "Outubro/2026"));
    }

    [Fact]
    [Trait("AC", "006-30")]
    public async Task Charts_UseTheWindowsFromTheClock_AndTakeMagnitudes()
    {
        _repository.MonthlyExpensesAsync(7, new DateOnly(2026, 7, 3), Arg.Any<CancellationToken>())
            .Returns([new MonthlyExpense("2026-10", -30.5m)]);
        _repository.CategoryExpensesAsync(7, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), Arg.Any<CancellationToken>())
            .Returns([new CategoryExpense("Alimentação", "Mercado", -12m, 2)]);
        _repository.WeekdayExpensesAsync(7, new DateOnly(2026, 7, 17), Arg.Any<CancellationToken>())
            .Returns([new WeekdayExpense(0, -5m, 1), new WeekdayExpense(3, -7m, 2)]);

        var charts = await new GetDashboardCharts(_repository, _clock).ExecuteAsync(7, 3, CancellationToken.None);

        charts.Monthly.ShouldBe([new MonthlyChartItem("2026-10", 30.5)]);
        charts.Categories.ShouldBe([new CategoryChartItem("Alimentação", "Mercado", 12, 2)]);
        charts.Weekdays.ShouldBe([new WeekdayChartItem("Sunday", 5, 1), new WeekdayChartItem("Wednesday", 7, 2)]);
    }

    [Fact]
    [Trait("AC", "006-38")]
    public async Task Charts_MonthsOnlyChangesTheMonthlyWindow()
    {
        await new GetDashboardCharts(_repository, _clock).ExecuteAsync(7, 12, CancellationToken.None);

        await _repository.Received(1).MonthlyExpensesAsync(7, new DateOnly(2025, 10, 6), Arg.Any<CancellationToken>());
        await _repository.Received(1).CategoryExpensesAsync(7, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1), Arg.Any<CancellationToken>());
        await _repository.Received(1).WeekdayExpensesAsync(7, new DateOnly(2026, 7, 17), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "006-61")]
    public async Task Alerts_UseTodayFromTheClock()
    {
        _repository.ListActiveSchedulesAsync(7, Arg.Any<CancellationToken>()).Returns(
            [new ScheduleRow(1, "Aluguel", 10m, "FIXO", "MENSAL", 15, null, new DateOnly(2026, 1, 1), null)]);

        var alerts = await new GetDashboardAlerts(_repository, _clock).ExecuteAsync(7, CancellationToken.None);

        alerts.Single().DaysLeft.ShouldBe(0);
    }

    [Fact]
    public async Task Recent_ReturnsWhatTheRepositoryHas()
    {
        IReadOnlyList<RecentTransaction> rows = [new(1, "x", -1m, "Despesa", Today, "m", "c")];
        _repository.ListRecentAsync(7, Arg.Any<CancellationToken>()).Returns(rows);

        (await new GetRecentTransactions(_repository).ExecuteAsync(7, CancellationToken.None)).ShouldBe(rows);
    }
}
