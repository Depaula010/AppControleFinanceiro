using MeuSecretario.Domain.Whatsapp.Analytics;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Analytics;

/// <summary>Regras da 012g que o harness não alcança (determinismo, limites do calendário e do <c>timedelta</c>).</summary>
public class AnalyticsDomainTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    [Fact]
    [Trait("AC", "012-702")]
    public void MonthlyComparison_TiedVariations_KeepTheOrderOfAppearance()
    {
        // O Flask itera um set (ordem muda a cada processo); aqui o desempate é a ordem em que a categoria aparece (spec §13.7).
        var rows = new MacroMonthTotal[]
        {
            new("2026-10", "Beta", -200), new("2026-10", "Alfa", -100), new("2026-09", "Alfa", -50), new("2026-09", "Beta", -100),
        };

        var text = MonthlyComparison.Format("2026-10", "2026-09", rows);

        text.IndexOf("*Beta:*", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("*Alfa:*", StringComparison.Ordinal));
        text.ShouldContain("🔴 *Beta:* R$ -200.00 (+100.0%)");
        text.ShouldContain("🔴 *Alfa:* R$ -100.00 (+100.0%)");
    }

    [Fact]
    [Trait("AC", "012-702")]
    public void MonthlyComparison_OnlyTheFiveBiggestChangesAppear()
    {
        var rows = Enumerable.Range(1, 7)
            .SelectMany(i => new[] { new MacroMonthTotal("2026-10", $"C{i}", -100 - i), new MacroMonthTotal("2026-09", $"C{i}", -100) })
            .ToList();

        var text = MonthlyComparison.Format("2026-10", "2026-09", rows);

        text.Split('\n').Count(line => line.Contains(" *C", StringComparison.Ordinal) && line.Contains("R$", StringComparison.Ordinal) && line.Contains('%', StringComparison.Ordinal))
            .ShouldBe(5);
        text.ShouldContain("*C7:*");
        text.ShouldNotContain("*C1:*");
    }

    [Theory]
    [InlineData(2026, 10, "2026-09")]
    [InlineData(2026, 1, "2025-12")]
    [InlineData(2024, 3, "2024-02")]
    public void MonthlyComparison_PreviousMonthKey_CrossesYears(int year, int month, string expected) =>
        MonthlyComparison.PreviousMonthKey(new DateOnly(year, month, 15)).ShouldBe(expected);

    [Fact]
    [Trait("AC", "012-701")]
    public void Forecast_WithoutHistory_HasNoProjection()
    {
        var data = new ForecastData(Today, 31, 6, [], [], 0, []);

        ForecastProjection.Calculate(data).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-701")]
    public void Forecast_TakesTheLargestOfLinearPendingAndEightyFivePercentOfTheAverage()
    {
        var history = new MonthTotal[] { new("2026-10", 100), new("2026-09", 300) };
        var pending = new PendingBill[]
        {
            new("Luz", 40, 10, "Conta Corrente", "Moradia"), new("Cartão", 25, 12, "Cartão de Crédito", "Financeiro"), new("Salário", 900, 5, "Conta Corrente", "Renda"),
        };

        var projection = ForecastProjection.Calculate(new ForecastData(Today, 31, 6, history, [], 70, pending))!;

        projection.NormalPending.ShouldBe(40);
        projection.CardPending.ShouldBe(25);
        projection.IncomePending.ShouldBe(900);
        projection.PendingTotal.ShouldBe(65);
        projection.DailyRate.ShouldBe(10);
        projection.HistoricalAverage.ShouldBe(200);
        projection.FinalProjection.ShouldBe(310); // linear: 10 * 31
    }

    [Theory]
    [InlineData(30, 2026, 9, 7)]
    [InlineData(7.5, 2026, 9, 30)] // date - timedelta usa só o piso dos dias
    [InlineData(-3, 2026, 10, 10)]
    [InlineData(0, 2026, 10, 7)]
    public void PieStart_UsesTheFloorOfTheDays(double days, int year, int month, int day) =>
        ChartRules.PieStart(Today, new ChartParam("x", days)).ShouldBe(new DateOnly(year, month, day));

    [Theory]
    [InlineData(null)]
    [InlineData(99_999_999_999d)]
    [InlineData(800_000d)] // fora do calendário (antes do ano 1)
    [InlineData(double.NaN)]
    public void PieStart_WhereThePythonRaises_IsNull(double? days) =>
        ChartRules.PieStart(Today, new ChartParam("x", days)).ShouldBeNull();

    [Fact]
    public void MonthsStart_MultipliesByThirtyDays() =>
        ChartRules.MonthsStart(Today, new ChartParam("6", 6)).ShouldBe(new DateOnly(2026, 4, 10));

    [Fact]
    public void PieData_DropsEmptyAndNonPositiveRowsAndReturnsNullWhenNothingIsLeft()
    {
        ChartRules.PieData([new PieRow("", 10), new PieRow("A", 0), new PieRow("B", -1)]).ShouldBeNull();
        ChartRules.PieData([]).ShouldBeNull();
        ChartRules.PieData([new PieRow("", 10), new PieRow("Moradia", 20)]).ShouldBe([("Moradia", 20d)]);
    }

    [Fact]
    public void ChartLabels_FollowTheFlaskFormats()
    {
        ChartRules.MonthLabel(new DateOnly(2026, 10, 1)).ShouldBe("Oct/26");
        ChartRules.MonthLabel(new DateOnly(2027, 1, 1)).ShouldBe("Jan/27");
        ChartRules.Currency(1234567.891).ShouldBe("R$ 1.234.567,89");
        ChartRules.Currency(0.5).ShouldBe("R$ 0,50");
    }

    [Fact]
    public void Balances_AddTheInitialBalanceToEachCumulativeValue()
    {
        var series = new LineSeries(100, [new LineRow(new DateOnly(2026, 8, 2), -50), new LineRow(new DateOnly(2026, 8, 15), 25.5)]);

        ChartRules.Balances(series).ShouldBe([(new DateOnly(2026, 8, 2), 50d), (new DateOnly(2026, 8, 15), 125.5)]);
    }

    [Fact]
    public void PotUsage_ZeroLimit_PrintsTheIntegerZero()
    {
        new PotUsage("P", 0, 10).UsageText.ShouldBe("0");
        new PotUsage("P", 200, 0.05).UsageText.ShouldBe("0.0");
        new PotUsage("P", 3, 1).UsageText.ShouldBe("33.3");
    }
}
