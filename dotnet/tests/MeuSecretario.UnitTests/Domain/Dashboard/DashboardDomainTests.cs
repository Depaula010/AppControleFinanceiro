using MeuSecretario.Domain.Dashboard;

namespace MeuSecretario.UnitTests.Domain.Dashboard;

public class DashboardSummaryCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    [Trait("AC", "006-10")]
    public void NoData_IsAllZero()
    {
        var summary = DashboardSummaryCalculator.Calculate([], 0m, 0m, Today);

        summary.ShouldBe(new DashboardSummary(0, 0, 0, 0, "Outubro/2026"));
    }

    [Fact]
    [Trait("AC", "006-11")]
    public void TotalBalance_IsTheSumOfTheAccountBalances()
    {
        DashboardSummaryCalculator.Calculate([120m, 50m], 0m, 0m, Today).TotalBalance.ShouldBe(170);
    }

    [Fact]
    [Trait("AC", "006-15")]
    public void IncomeAndExpense_AreMagnitudes()
    {
        var summary = DashboardSummaryCalculator.Calculate([], 100m, -250.50m, Today);

        summary.MonthIncome.ShouldBe(100);
        summary.MonthExpense.ShouldBe(250.5);
    }

    [Fact]
    [Trait("AC", "006-16")]
    public void MonthNet_CanBeNegative()
    {
        DashboardSummaryCalculator.Calculate([], 100m, -250.50m, Today).MonthNet.ShouldBe(-150.5);
    }

    [Fact]
    [Trait("AC", "006-17")]
    public void MonthNet_IsRoundedLikePython()
    {
        // 0.30 - 0.10 = 0.19999999999999998 em double; round(…, 2) = 0.2
        DashboardSummaryCalculator.Calculate([], 0.30m, -0.10m, Today).MonthNet.ShouldBe(0.2);
    }

    [Theory]
    [Trait("AC", "006-18")]
    [InlineData(1, "Janeiro/2026")]
    [InlineData(2, "Fevereiro/2026")]
    [InlineData(3, "Março/2026")]
    [InlineData(4, "Abril/2026")]
    [InlineData(5, "Maio/2026")]
    [InlineData(6, "Junho/2026")]
    [InlineData(7, "Julho/2026")]
    [InlineData(8, "Agosto/2026")]
    [InlineData(9, "Setembro/2026")]
    [InlineData(10, "Outubro/2026")]
    [InlineData(11, "Novembro/2026")]
    [InlineData(12, "Dezembro/2026")]
    public void ReferenceMonth_IsPortugueseWithYear(int month, string expected)
    {
        DashboardMonths.Reference(new DateOnly(2026, month, 1)).ShouldBe(expected);
    }
}

public class ChartsWindowTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    [Fact]
    [Trait("AC", "006-30")]
    public void MonthlyStart_IsFirstOfMonthMinusThirtyDaysPerMonth()
    {
        ChartsWindow.MonthlyStart(Today, 3).ShouldBe(new DateOnly(2026, 7, 3));
        ChartsWindow.MonthlyStart(Today, 1).ShouldBe(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void MonthBounds_AreFirstDayAndNextFirstDay()
    {
        ChartsWindow.MonthStart(Today).ShouldBe(new DateOnly(2026, 10, 1));
        ChartsWindow.NextMonthStart(new DateOnly(2026, 12, 31)).ShouldBe(new DateOnly(2027, 1, 1));
    }

    [Fact]
    [Trait("AC", "006-36")]
    public void WeekdayStart_IsNinetyDaysBack()
    {
        ChartsWindow.WeekdayStart(Today).ShouldBe(new DateOnly(2026, 7, 17));
    }

    [Theory]
    [Trait("AC", "006-37")]
    [InlineData(0, "Sunday")]
    [InlineData(1, "Monday")]
    [InlineData(2, "Tuesday")]
    [InlineData(3, "Wednesday")]
    [InlineData(4, "Thursday")]
    [InlineData(5, "Friday")]
    [InlineData(6, "Saturday")]
    public void WeekdayNames_AreEnglishWithoutPadding(int dow, string expected)
    {
        WeekdayNames.For(dow).ShouldBe(expected);
    }
}

public class MonthsParameterTests
{
    [Theory]
    [Trait("AC", "006-30")]
    [Trait("AC", "006-32")]
    [InlineData(null, 3)]
    [InlineData("abc", 3)]
    [InlineData("", 3)]
    [InlineData("2.5", 3)]
    [InlineData("1__0", 3)]
    [InlineData("_1", 3)]
    [InlineData(" 5", 5)]
    [InlineData("+5", 5)]
    [InlineData("1_0", 10)]
    [InlineData("007", 7)]
    [InlineData("1", 1)]
    [InlineData("12", 12)]
    public void Parse_FollowsPythonInt_WithFallbackToThree(string? raw, int expected)
    {
        MonthsParameter.Parse(raw).ShouldBe(new MonthsResult.Ok(expected));
    }

    [Theory]
    [Trait("AC", "006-31")]
    [InlineData("0")]
    [InlineData("13")]
    [InlineData("-1")]
    [InlineData("99999999999999999999")]
    public void Parse_OutsideOneToTwelve_IsOutOfRange(string raw)
    {
        MonthsParameter.Parse(raw).ShouldBeOfType<MonthsResult.OutOfRange>();
    }
}

public class DashboardAlertsTests
{
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static ScheduleRow Row(
        string? periodicity = "MENSAL", int? day = 15, int? month = null, DateOnly? start = null, decimal? amount = 100m, int id = 1) =>
        new(id, "Aluguel", amount, "FIXO", periodicity, day, month, start ?? new DateOnly(2026, 1, 1), "Aluguel / Condomínio");

    private static DashboardAlert Single(ScheduleRow row, DateOnly? today = null)
    {
        var alerts = DashboardAlerts.Build([row], today ?? Today);
        alerts.Count.ShouldBe(1);
        return alerts[0];
    }

    [Fact]
    [Trait("AC", "006-51")]
    public void Monthly_DueToday_IsDanger()
    {
        var alert = Single(Row(day: 15));

        alert.DaysLeft.ShouldBe(0);
        alert.Level.ShouldBe("danger");
        alert.DueDate.ShouldBe(Today);
    }

    [Fact]
    [Trait("AC", "006-51")]
    public void Monthly_DayAlreadyPassed_GoesToNextMonth_AndIsDroppedWhenFar()
    {
        DashboardAlerts.Build([Row(day: 10)], Today).ShouldBeEmpty();
        Single(Row(day: 3), new DateOnly(2026, 10, 28)).DueDate.ShouldBe(new DateOnly(2026, 11, 3));
    }

    [Fact]
    [Trait("AC", "006-51")]
    public void Monthly_Day31_FallsOnLastDayOfShortMonth()
    {
        Single(Row(day: 31), new DateOnly(2026, 11, 28)).DueDate.ShouldBe(new DateOnly(2026, 11, 30));
    }

    [Fact]
    [Trait("AC", "006-60")]
    public void Monthly_CrossesTheYear()
    {
        var alert = Single(Row(day: 3), new DateOnly(2026, 12, 28));

        alert.DueDate.ShouldBe(new DateOnly(2027, 1, 3));
        alert.DaysLeft.ShouldBe(6);
    }

    [Fact]
    [Trait("AC", "006-52")]
    public void Window_IncludesSevenDaysAndExcludesEight()
    {
        Single(Row(day: 22)).Level.ShouldBe("info");
        DashboardAlerts.Build([Row(day: 23)], Today).ShouldBeEmpty();
    }

    [Theory]
    [Trait("AC", "006-53")]
    [InlineData(0, "danger")]
    [InlineData(1, "warning")]
    [InlineData(2, "warning")]
    [InlineData(3, "info")]
    [InlineData(7, "info")]
    public void Level_FollowsDaysLeft(int daysLeft, string expected)
    {
        DashboardAlerts.Level(daysLeft).ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "006-54")]
    public void Annual_ThisYearWhenNotPassed_NextYearWhenPassed()
    {
        Single(Row("ANUAL", day: 18, month: 10)).DueDate.ShouldBe(new DateOnly(2026, 10, 18));
        DashboardAlerts.Build([Row("ANUAL", day: 14, month: 10)], Today).ShouldBeEmpty();
        NextDue(Row("ANUAL", day: 14, month: 10)).ShouldBe(new DateOnly(2027, 10, 14));
    }

    [Fact]
    [Trait("AC", "006-54")]
    public void Annual_Feb29_FallsOnFeb28_InCommonYears()
    {
        NextDue(Row("ANUAL", day: 29, month: 2), new DateOnly(2026, 2, 20)).ShouldBe(new DateOnly(2026, 2, 28));
    }

    [Theory]
    [Trait("AC", "006-54")]
    [InlineData(null, 5)]
    [InlineData(10, null)]
    [InlineData(10, 13)]
    [InlineData(10, 0)]
    public void Annual_WithoutValidMonthOrDay_IsIgnored(int? day, int? month)
    {
        NextDue(Row("ANUAL", day: day, month: month)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "006-55")]
    public void Weekly_PastStart_AdvancesByWholeCycles()
    {
        NextDue(Row("SEMANAL", start: Today.AddDays(-10))).ShouldBe(Today.AddDays(4));
        NextDue(Row("SEMANAL", start: Today.AddDays(-14))).ShouldBe(Today);
    }

    [Fact]
    [Trait("AC", "006-55")]
    public void Biweekly_OutsideTheWindow_IsDropped()
    {
        var row = Row("QUINZENAL", start: Today.AddDays(-1));

        NextDue(row).ShouldBe(Today.AddDays(13));
        DashboardAlerts.Build([row], Today).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "006-55")]
    public void Daily_IsAlwaysToday_AndFutureStartIsKept()
    {
        Single(Row("DIARIA", start: Today.AddDays(-40))).DaysLeft.ShouldBe(0);
        Single(Row("SEMANAL", start: Today.AddDays(3))).DaysLeft.ShouldBe(3);
    }

    [Fact]
    [Trait("AC", "006-56")]
    public void Weekly_WithoutStartDate_IsIgnored()
    {
        var row = Row("SEMANAL") with { StartDate = null };

        DashboardAlerts.Build([row], Today).ShouldBeEmpty();
    }

    [Fact]
    public void NullPeriodicity_IsMonthly()
    {
        NextDue(Row(null, day: 16)).ShouldBe(new DateOnly(2026, 10, 16));
    }

    [Fact]
    [Trait("AC", "006-57")]
    public void Alerts_AreSortedByDaysLeft_ThenDay_ThenId()
    {
        var alerts = DashboardAlerts.Build(
            [Row(day: 17, id: 9), Row(day: 16, id: 5), Row(day: 16, id: 3), Row(day: 15, id: 7)], Today);

        alerts.Select(a => a.Id).ShouldBe(["7", "3", "5", "9"]);
    }

    [Theory]
    [Trait("AC", "006-58")]
    [InlineData(null, null)]
    [InlineData(0.0, null)]
    [InlineData(1200.5, 1200.5)]
    public void Amount_NullOrZeroIsNull(double? amount, double? expected)
    {
        Single(Row(amount: (decimal?)amount)).Amount.ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "006-59")]
    public void Alert_CarriesRawFields()
    {
        var alert = Single(Row(day: 16));

        alert.Id.ShouldBe("1");
        alert.Day.ShouldBe(16);
        alert.Type.ShouldBe("FIXO");
        alert.Subcategory.ShouldBe("Aluguel / Condomínio");
    }

    private static DateOnly? NextDue(ScheduleRow row, DateOnly? today = null) => DashboardAlerts.NextDue(row, today ?? Today);
}
