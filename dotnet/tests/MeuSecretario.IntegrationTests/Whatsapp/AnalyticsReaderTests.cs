using MeuSecretario.Domain.Whatsapp.Analytics;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>SQL real das análises, da previsão e do gráfico do WhatsApp (spec 012 §6.g).</summary>
[Trait("Category", "Docker")]
public class AnalyticsReaderTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    // Quarta-feira, como no harness do Flask.
    private static readonly DateOnly Today = new(2026, 10, 7);

    private AutomationEnv _env = default!;
    private AnalyticsReader _reader = default!;
    private int _user;
    private int _other;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _reader = new AnalyticsReader(postgres.Connections);
        _user = await _env.SeedUserAsync();
        _other = await _env.SeedUserAsync("5511999990001", "outra-chave");
        _account = await _env.Data.InsertAccountAsync(_user, "Nubank");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<int> Expense(decimal value, string date, string description = "teste", bool consolidated = true, int? user = null, int? account = null) =>
        _env.Data.InsertTransactionAsync(user ?? _user, account ?? _account, valor: value, data: date, descricao: description, consolidada: consolidated);

    [Fact]
    [Trait("AC", "012-701")]
    public async Task SpendingAnalysis_CollectsTheMonthWeekdaysPotsTopExpensesAndFixedBills()
    {
        await Expense(-100m, "2026-10-05", "Mercado"); // segunda
        await Expense(-40.5m, "2026-10-07", "Padaria"); // quarta
        await Expense(-60m, "2026-09-10", "Antiga");
        await Expense(-999m, "2026-10-06", "Não consolidada", consolidated: false);
        await Expense(-5m, "2026-10-06", "De outro", user: _other, account: await _env.Data.InsertAccountAsync(_other, "Alheia"));
        await _env.Data.InsertTransactionAsync(_user, _account, tipo: "Renda", valor: 3000m, data: "2026-10-01", descricao: "Salário", subcategoryId: TransactionsTestData.IncomeSubcategory);
        await _env.InsertPotAsync(_user, "Outros", 200m, "MENSAL", TransactionsTestData.ExpenseSubcategory);
        await _env.InsertScheduleAsync(_user, _account, "Netflix", amount: 55.9m);
        await _env.InsertScheduleAsync(_user, _account, "Aluguel", amount: 1500m);
        await _env.InsertScheduleAsync(_user, _account, "Tênis", type: "PARCELADO", amount: 300m, total: 3);
        await _env.InsertScheduleAsync(_user, _account, "Velha", amount: 10m, active: false);

        var data = await _reader.GetSpendingAnalysisAsync(_user, Today, CancellationToken.None);

        data.CurrentMonth.ShouldBe("2026-10");
        data.Comparison.ShouldBe([new MonthTotal("2026-10", -140.5), new MonthTotal("2026-09", -60)], ignoreOrder: true);
        data.Categories.Count.ShouldBe(1);
        data.Categories[0].Total.ShouldBe(-140.5);
        data.Categories[0].Quantity.ShouldBe(2);
        data.Weekdays.Select(w => (w.Day, w.Total, w.Quantity)).ShouldBe([("Monday", -100d, 1), ("Wednesday", -40.5, 1), ("Thursday", -60d, 1)]);
        data.Pots.Select(p => (p.Name, p.Limit, p.Spent)).ShouldBe([("Outros", 200d, -140.5)]);
        data.TopExpenses.Select(e => e.Description).ShouldBe(["Mercado", "Padaria"]); // desvio 233: ABS(valor) DESC, os maiores primeiro
        data.TopExpenses[0].Date.ShouldBe(new DateOnly(2026, 10, 5));
        data.FixedCharges.Select(c => (c.Description, c.Value, c.Periodicity)).ShouldBe([("Aluguel", 1500d, "MENSAL"), ("Netflix", 55.9, "MENSAL")]);
    }

    [Fact]
    [Trait("AC", "012-233")]
    public async Task SpendingAnalysis_TopExpenses_AreTheLargestByAbsoluteValue_TiesById()
    {
        await Expense(-10m, "2026-10-01", "Pequena");
        await Expense(-500m, "2026-10-02", "Grande");
        await Expense(-80m, "2026-10-03", "Media A");
        var tieBreaker = await Expense(-80m, "2026-10-04", "Media B");
        await Expense(-300m, "2026-10-05", "Alta");
        await Expense(-20m, "2026-10-06", "Baixa");
        await Expense(-5000m, "2026-09-06", "Mes anterior");
        tieBreaker.ShouldBeGreaterThan(0);

        var data = await _reader.GetSpendingAnalysisAsync(_user, Today, CancellationToken.None);

        data.TopExpenses.Select(e => e.Description).ShouldBe(["Grande", "Alta", "Media A", "Media B", "Baixa"]);
    }

    [Fact]
    [Trait("AC", "012-701")]
    public async Task SpendingAnalysis_WeekdaysUseTheLastNinetyDaysOnly()
    {
        await Expense(-10m, "2026-07-09"); // 90 dias antes de 7/10 é 9/7: entra
        await Expense(-20m, "2026-07-08"); // fora

        var data = await _reader.GetSpendingAnalysisAsync(_user, Today, CancellationToken.None);

        data.Weekdays.Sum(w => w.Quantity).ShouldBe(1);
        data.Weekdays[0].Total.ShouldBe(-10);
    }

    [Fact]
    [Trait("AC", "012-701")]
    public async Task ForecastData_HistoryMonthToDateFixedBillsAndPendingBills()
    {
        await Expense(-300m, "2026-09-10");
        await Expense(-200m, "2026-08-20");
        await Expense(-50m, "2026-10-02", "Mercado");
        await Expense(-1m, "2026-03-01", "Muito antiga");
        await _env.InsertScheduleAsync(_user, _account, "Aluguel", amount: -1500m, day: 10);
        await _env.InsertScheduleAsync(_user, _account, "Mercado", amount: -80m, day: 3); // já paga neste mês (descrição bate)
        await _env.InsertScheduleAsync(_user, _account, "Tênis", type: "PARCELADO", amount: -150m, total: 6, executed: 2);
        await _env.InsertScheduleAsync(_user, _account, "Anual", periodicity: "ANUAL", amount: -90m, month: 12);

        var data = await _reader.GetForecastDataAsync(_user, Today, CancellationToken.None);

        data.Today.ShouldBe(Today);
        data.DaysInMonth.ShouldBe(31);
        data.HistoryMonths.ShouldBe(6);
        data.MonthlyTotals.ShouldBe([new MonthTotal("2026-10", -50), new MonthTotal("2026-09", -300), new MonthTotal("2026-08", -200)]);
        data.SpentToDate.ShouldBe(-50);
        data.FixedCharges.Select(c => c.Description).ShouldBe(["Mercado", "Anual", "Tênis", "Aluguel"]); // ORDER BY valor_previsto DESC
        data.Pending.Select(p => (p.Description, p.Value, p.Day)).ShouldBe([("Aluguel", -1500d, 10)]);
        data.Pending[0].GroupName.ShouldNotBeNull();
        data.Pending[0].AccountType.ShouldBe("Conta Corrente");

    }

    [Fact]
    [Trait("AC", "012-701")]
    public async Task ForecastData_WithoutExpenses_HasZeroMonthToDate_AndNoHistory()
    {
        var data = await _reader.GetForecastDataAsync(_user, Today, CancellationToken.None);

        data.MonthlyTotals.ShouldBeEmpty();
        data.SpentToDate.ShouldBe(0);
        data.Pending.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-702")]
    public async Task MonthlyComparison_GroupsByMacroAndMonth_AbsoluteTotals_LargestFirst()
    {
        await Expense(-100m, "2026-10-05");
        await Expense(-30m, "2026-09-05");
        await Expense(-7m, "2026-08-05"); // fora dos dois meses
        await Expense(-500m, "2026-10-06", consolidated: false);

        var rows = await _reader.GetMonthlyComparisonAsync(_user, "2026-10", "2026-09", CancellationToken.None);

        rows.Select(r => (r.Month, r.Total)).ShouldBe([("2026-10", 100d), ("2026-09", 30d)]); // desvio 232: valor absoluto, ORDER BY total DESC
        (await _reader.GetMonthlyComparisonAsync(_other, "2026-10", "2026-09", CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-703")]
    public async Task PieRows_AreAbsoluteConsolidatedExpensesPerMacro_FromTheStartDate()
    {
        await Expense(-100m, "2026-10-05");
        await Expense(-50m, "2026-10-06", consolidated: false);
        await Expense(-70m, "2026-09-06"); // antes do início
        await _env.Data.InsertTransactionAsync(_user, _account, tipo: "Renda", valor: 900m, data: "2026-10-01", subcategoryId: TransactionsTestData.IncomeSubcategory);

        var rows = await _reader.GetPieRowsAsync(_user, new DateOnly(2026, 9, 7), CancellationToken.None);

        rows.Select(r => r.Total).ShouldBe([100d]); // desvio 231: a não consolidada (-50) fica fora
        rows[0].Category.ShouldNotBeNullOrEmpty();
        (await _reader.GetPieRowsAsync(_user, new DateOnly(2026, 10, 7), CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-703")]
    public async Task BarRows_SumExpensesAndIncomePerMonth_Chronologically()
    {
        await Expense(-100m, "2026-08-31");
        await Expense(-40m, "2026-09-01");
        await _env.Data.InsertTransactionAsync(_user, _account, tipo: "Renda", valor: 900m, data: "2026-09-15", subcategoryId: TransactionsTestData.IncomeSubcategory);
        await Expense(-1m, "2026-03-01"); // antes do início
        await Expense(-77m, "2026-09-02", consolidated: false); // desvio 231: não consolidada fica fora
        await _env.Data.InsertTransactionAsync(
            _user, _account, tipo: "Renda", valor: 55m, data: "2026-09-03", subcategoryId: TransactionsTestData.IncomeSubcategory, consolidada: false);

        var rows = await _reader.GetBarRowsAsync(_user, new DateOnly(2026, 4, 10), CancellationToken.None);

        rows.ShouldBe([new BarRow(new DateOnly(2026, 8, 1), 100, 0), new BarRow(new DateOnly(2026, 9, 1), 40, 900)]);
    }

    [Fact]
    [Trait("AC", "012-231")]
    public async Task LineSeries_StartsFromTheRealBalance_ActiveAccountsAndConsolidatedHistory()
    {
        await _env.Data.ExecuteAsync("UPDATE contas SET saldo_inicial = 1000 WHERE id = @account", new { account = _account });
        var inactive = await _env.Data.InsertAccountAsync(_user, "Inativa", ativa: false);
        await _env.Data.ExecuteAsync("UPDATE contas SET saldo_inicial = 5 WHERE nome_conta = 'Inativa'");
        await Expense(-999m, "2026-03-01"); // antes do início, consolidada: entra no saldo inicial da janela
        await Expense(-111m, "2026-03-02", consolidated: false); // antes do início, não consolidada: fora
        await Expense(-100m, "2026-08-01");
        await Expense(-20m, "2026-08-15");
        await Expense(-33m, "2026-08-20", consolidated: false); // não consolidada: fora do acumulado
        await Expense(-44m, "2026-08-21", account: inactive); // conta inativa: fora
        await _env.Data.InsertTransactionAsync(_user, _account, tipo: "Renda", valor: 500m, data: "2026-08-15", subcategoryId: TransactionsTestData.IncomeSubcategory);

        var series = await _reader.GetLineSeriesAsync(_user, new DateOnly(2026, 4, 10), CancellationToken.None);

        // saldo_inicial das contas ativas (1000) + consolidadas anteriores à janela (-999); a conta inativa (5) não entra.
        series.InitialBalance.ShouldBe(1);
        series.Rows.Select(r => (r.Date, r.Cumulative)).ShouldBe(
            [(new DateOnly(2026, 8, 1), -100d), (new DateOnly(2026, 8, 15), 380d), (new DateOnly(2026, 8, 15), 380d)]);
        (await _reader.GetLineSeriesAsync(_other, new DateOnly(2026, 4, 10), CancellationToken.None)).Rows.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-231")]
    public async Task LineSeries_FinalBalanceMatchesTheBalanceOfTheActiveAccounts()
    {
        await _env.Data.ExecuteAsync("UPDATE contas SET saldo_inicial = 250 WHERE id = @account", new { account = _account });
        await Expense(-50m, "2026-01-10");
        await Expense(-25m, "2026-08-10");
        await _env.Data.InsertTransactionAsync(_user, _account, tipo: "Renda", valor: 300m, data: "2026-09-10", subcategoryId: TransactionsTestData.IncomeSubcategory);

        var series = await _reader.GetLineSeriesAsync(_user, new DateOnly(2026, 4, 10), CancellationToken.None);

        var final = series.InitialBalance + series.Rows[^1].Cumulative;
        final.ShouldBe(250 - 50 - 25 + 300);
    }
}
