using System.Globalization;
using System.Text.Json;
using MeuSecretario.Domain.Bills;
using MeuSecretario.Domain.Common;

namespace MeuSecretario.UnitTests.Domain.Bills;

internal static class BillsFixtures
{
    public static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "bills-fixtures.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    public static double D(JsonElement element, string name) =>
        double.Parse(element.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
}

public class PyRoundTests
{
    [Fact]
    [Trait("AC", "005-97")]
    public void Round2_MatchesPythonRoundForEveryFixture()
    {
        var cases = BillsFixtures.Load().GetProperty("round2").EnumerateArray().ToList();
        cases.Count.ShouldBeGreaterThan(300);
        foreach (var c in cases)
        {
            var value = BillsFixtures.D(c, "value");
            PyRound.Round2(value).ShouldBe(BillsFixtures.D(c, "rounded"), $"round({value:R}, 2)");
        }
    }

    [Theory]
    [Trait("AC", "005-97")]
    [InlineData(0.125, 0.12)]
    [InlineData(2.675, 2.67)]
    [InlineData(1.115, 1.11)]
    [InlineData(0.5, 0.5)]
    [InlineData(-0.125, -0.12)]
    [InlineData(-2.675, -2.67)]
    [InlineData(100.125, 100.12)]
    [InlineData(503.3333333333333, 503.33)]
    public void Round2_IsHalfToEvenOnTheExactBinaryValue(double value, double expected) =>
        PyRound.Round2(value).ShouldBe(expected);

    [Fact]
    public void Round2_LeavesNonFiniteAndHugeValuesAlone()
    {
        PyRound.Round2(double.NaN).ShouldBe(double.NaN);
        PyRound.Round2(double.PositiveInfinity).ShouldBe(double.PositiveInfinity);
        PyRound.Round2(1e30).ShouldBe(1e30);
    }
}

public class ReserveFactorTests
{
    [Fact]
    [Trait("AC", "005-104")]
    public void For_MatchesPythonForMonthsOneToThirtySix()
    {
        foreach (var c in BillsFixtures.Load().GetProperty("factors").EnumerateArray())
        {
            var months = c.GetProperty("months").GetInt32();
            ReserveFactor.For(Periodicities.Monthly, months).ShouldBe(c.GetProperty("monthly").GetInt32());
            ReserveFactor.For(Periodicities.Annual, months).ShouldBe(1);
            ReserveFactor.For(Periodicities.Weekly, months).ShouldBe(c.GetProperty("weekly").GetInt32(), $"semanal N={months}");
            ReserveFactor.For(Periodicities.Biweekly, months).ShouldBe(c.GetProperty("biweekly").GetInt32());
            ReserveFactor.For(Periodicities.Daily, months).ShouldBe(c.GetProperty("daily").GetInt32());
        }
    }

    [Fact]
    [Trait("AC", "005-103")]
    public void Weekly_FiftyMonths_RoundsHalfToEvenTo216()
    {
        ReserveFactor.For(Periodicities.Weekly, 50).ShouldBe(216);
    }

    [Fact]
    public void UnknownPeriodicity_ContributesNothing()
    {
        ReserveFactor.For("BIMESTRAL", 6).ShouldBe(0);
    }
}

public class EmergencyReserveTests
{
    [Fact]
    [Trait("AC", "005-100")]
    public void Calculate_MatchesGetReservaStatusForEveryFixture()
    {
        var cases = BillsFixtures.Load().GetProperty("reserve").EnumerateArray().ToList();
        cases.Count.ShouldBeGreaterThan(100);
        foreach (var c in cases)
        {
            var months = c.GetProperty("months").ValueKind == JsonValueKind.Null ? 6 : c.GetProperty("months").GetInt32();
            var totals = c.GetProperty("totals").EnumerateArray()
                .Select(t => new PeriodTotal(t.GetProperty("periodicity").GetString()!, BillsFixtures.D(t, "total")))
                .ToList();

            var result = EmergencyReserve.Calculate(months, totals);

            result.Total.ShouldBe(BillsFixtures.D(c, "total"), $"meses={months}");
            result.MonthlyEquivalent.ShouldBe(BillsFixtures.D(c, "monthly_equivalent"), $"meses={months}");
        }
    }

    [Fact]
    [Trait("AC", "005-93")]
    public void Calculate_SpecExample_Is3020()
    {
        // FIXO MENSAL 100, ANUAL 1200, SEMANAL 10, QUINZENAL 50, DIARIA 2, com 6 meses
        var result = EmergencyReserve.Calculate(6,
        [
            new(Periodicities.Annual, 1200), new(Periodicities.Daily, 2), new(Periodicities.Monthly, 100),
            new(Periodicities.Biweekly, 50), new(Periodicities.Weekly, 10),
        ]);

        result.Total.ShouldBe(3020.0);
        PyRound.Round2(result.MonthlyEquivalent).ShouldBe(503.33);
    }

    [Fact]
    [Trait("AC", "005-100")]
    public void Calculate_NothingMarked_IsZero()
    {
        var result = EmergencyReserve.Calculate(6, []);

        result.Total.ShouldBe(0);
        result.MonthlyEquivalent.ShouldBe(0);
    }

    [Fact]
    public void Calculate_ZeroMonths_HasNoDivisionByZero()
    {
        EmergencyReserve.Calculate(0, [new(Periodicities.Annual, 100)]).MonthlyEquivalent.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "005-102")]
    public void Calculate_NegativeAmount_IsSummedAsIs()
    {
        EmergencyReserve.Calculate(6, [new(Periodicities.Monthly, -10)]).Total.ShouldBe(-60);
    }

    [Fact]
    [Trait("AC", "005-101")]
    public void Impact_IsAmountTimesTheSameFactor()
    {
        EmergencyReserve.Impact(10, Periodicities.Weekly, 50).ShouldBe(2160);
        EmergencyReserve.Impact(100, Periodicities.Monthly, 6).ShouldBe(600);
    }
}

public class BillsSummaryCalculatorTests
{
    private static SummaryRow Expense(string periodicity, double total) => new(periodicity, false, total);

    private static SummaryRow Income(string periodicity, double total) => new(periodicity, true, total);

    [Fact]
    [Trait("AC", "005-90")]
    public void Calculate_AppliesTheMonthlyFactors()
    {
        var summary = BillsSummaryCalculator.Calculate(
            [
                Expense(Periodicities.Annual, 1200), Expense(Periodicities.Daily, 2), Expense(Periodicities.Monthly, 100),
                Income(Periodicities.Monthly, 3000), Expense(Periodicities.Biweekly, 50), Expense(Periodicities.Weekly, 10),
            ],
            reserveTotal: 1,
            months: 6);

        // 100 + 2×30 + 50×2,16 + 10×4,33 na ordem recebida, como double
        var expected = 0.0;
        expected += 2 * 30.0;
        expected += 100;
        expected += 50 * 2.16;
        expected += 10 * 4.33;
        summary.MonthlyExpenses.ShouldBe(expected);
        Math.Abs(summary.MonthlyExpenses - 311.3).ShouldBeLessThan(1e-9);
        summary.AnnualExpenses.ShouldBe(1200);
        summary.MonthlyIncome.ShouldBe(3000);
    }

    [Fact]
    [Trait("AC", "005-91")]
    public void Calculate_AnnualIncome_IsInNoTotal()
    {
        var summary = BillsSummaryCalculator.Calculate([Income(Periodicities.Annual, 500)], 0, 6);

        summary.MonthlyIncome.ShouldBe(0);
        summary.MonthlyExpenses.ShouldBe(0);
        summary.AnnualExpenses.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "005-95")]
    public void Calculate_NothingMarked_FallsBackToMonthlyExpensesTimesMonths()
    {
        var summary = BillsSummaryCalculator.Calculate([Expense(Periodicities.Monthly, 1000)], 0, 6);

        summary.ReserveGoal.ShouldBe(6000.0);
        summary.ReserveMonthlyEquivalent.ShouldBe(1000.0);
        summary.ReserveMonths.ShouldBe(6);
    }

    [Fact]
    [Trait("AC", "005-95")]
    public void Calculate_NoBills_IsAllZero()
    {
        var summary = BillsSummaryCalculator.Calculate([], 0, 6);

        summary.ShouldBe(new BillsSummary(0, 0, 0, 0, 0, 6));
    }

    [Fact]
    [Trait("AC", "005-93")]
    public void Calculate_UsesTheReserveTotalAndRoundsOnlyTheGoal()
    {
        var summary = BillsSummaryCalculator.Calculate([Expense(Periodicities.Monthly, 100.123)], 3020, 6);

        summary.ReserveGoal.ShouldBe(3020.0);
        summary.ReserveMonthlyEquivalent.ShouldBe(503.33);
        summary.MonthlyExpenses.ShouldBe(100.123);
    }

    [Fact]
    [Trait("AC", "005-96")]
    public void Calculate_ZeroMonths_HasZeroEquivalentAndNoDivisionByZero()
    {
        var summary = BillsSummaryCalculator.Calculate([Expense(Periodicities.Monthly, 100)], 0, 0);

        summary.ReserveMonthlyEquivalent.ShouldBe(0);
        summary.ReserveGoal.ShouldBe(0);
    }

    [Fact]
    public void Calculate_IncomeRowsDoNotTriggerTheFallback()
    {
        var summary = BillsSummaryCalculator.Calculate([Income(Periodicities.Monthly, 3000)], 0, 6);

        summary.ReserveGoal.ShouldBe(0);
    }
}

public class BillBasicsTests
{
    [Fact]
    public void Types_AreExactAndInTheMessageOrder()
    {
        BillTypes.All.ShouldBe(["FIXO", "LEMBRETE_VARIAVEL", "PARCELADO"]);
        BillTypes.IsValid("FIXO").ShouldBeTrue();
        BillTypes.IsValid("fixo").ShouldBeFalse();
        BillTypes.IsValid(" FIXO ").ShouldBeFalse();
        BillTypes.RequiresAmount("FIXO").ShouldBeTrue();
        BillTypes.RequiresAmount("PARCELADO").ShouldBeTrue();
        BillTypes.RequiresAmount("LEMBRETE_VARIAVEL").ShouldBeFalse();
    }

    [Fact]
    public void Periodicities_AreExactAndInTheMessageOrder()
    {
        Periodicities.All.ShouldBe(["DIARIA", "SEMANAL", "QUINZENAL", "MENSAL", "ANUAL"]);
        Periodicities.IsValid("MENSAL").ShouldBeTrue();
        Periodicities.IsValid("mensal").ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "005-01")]
    [InlineData("Renda", "Salário", true)]
    [InlineData("Geral", "Receitas Gerais", true)]
    [InlineData("Geral", "Despesas Gerais", false)]
    [InlineData("Despesa Essencial", "Receitas Gerais", false)]
    [InlineData(null, null, false)]
    public void IsIncome_IsRendaOrReceitasGerais(string? group, string? macro, bool expected) =>
        BillKinds.IsIncome(group, macro).ShouldBe(expected);

    [Fact]
    public void Description_CountsCodePointsNotUtf16Units()
    {
        BillDescription.Fits(new string('a', 255)).ShouldBeTrue();
        BillDescription.Fits(new string('a', 256)).ShouldBeFalse();
        BillDescription.Fits(string.Concat(Enumerable.Repeat("😀", 255))).ShouldBeTrue();
        BillDescription.Fits(string.Concat(Enumerable.Repeat("😀", 256))).ShouldBeFalse();
    }
}
