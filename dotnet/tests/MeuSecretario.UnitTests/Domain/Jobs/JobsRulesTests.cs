using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.UnitTests.Domain.Jobs;

public class JobsRulesTests
{
    private static ScheduleInfo Schedule(
        string kind = ScheduleKinds.Fixed, int day = 10, string periodicity = "MENSAL", int? month = null, int notify = 3,
        int executed = 0, int? total = null, string group = "Despesa Essencial", decimal? amount = 100m) =>
        new(1, 2, 3, 4, "Luz", amount, kind, periodicity, day, month, total, executed, notify, group, "Conta Corrente", "5511");

    // ---------------------------------------------------------------- JobClock

    [Fact]
    [Trait("AC", "013-20")]
    public void HourMinute_DropsSecondsAndMilliseconds() =>
        JobClock.HourMinute(new DateTime(2026, 3, 18, 7, 0, 42, 900)).ShouldBe(new TimeOnly(7, 0));

    [Fact]
    [Trait("AC", "013-70")]
    public void Window_WrapsAroundMidnight()
    {
        JobClock.Window(new TimeOnly(8, 0), 5).ShouldBe((new TimeOnly(7, 55), new TimeOnly(8, 5)));
        var (min, max) = JobClock.Window(new TimeOnly(23, 58), 5);
        min.ShouldBe(new TimeOnly(23, 53));
        max.ShouldBe(new TimeOnly(0, 3));
    }

    [Theory]
    [Trait("AC", "013-70")]
    [InlineData("2026-02-28", true)]
    [InlineData("2028-02-28", false)]
    [InlineData("2028-02-29", true)]
    [InlineData("2026-04-30", true)]
    [InlineData("2026-04-29", false)]
    [InlineData("2026-12-31", true)]
    public void IsLastDayOfMonth_HandlesShortMonthsAndLeapYears(string date, bool expected) =>
        JobClock.IsLastDayOfMonth(DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);

    // ---------------------------------------------------------------- dinheiro e números

    [Fact]
    [Trait("AC", "013-45")]
    public void PtBrMoney_FormatsLikeFormatarMoeda()
    {
        PtBrMoney.Format(1234.56m).ShouldBe("R$ 1.234,56");
        PtBrMoney.Format((decimal?)null).ShouldBe("R$ 0,00");
        PtBrMoney.Format(0m).ShouldBe("R$ 0,00");
        PtBrMoney.Format(1234567.8m).ShouldBe("R$ 1.234.567,80");
        PtBrMoney.Format(1234.5).ShouldBe("R$ 1.234,50");
    }

    [Fact]
    [Trait("AC", "013-72")]
    public void PyNumber_FollowsPythonRoundingAndGrouping()
    {
        PyNumber.Fixed(0.125, 2).ShouldBe("0.12");
        PyNumber.Fixed(2.675, 2).ShouldBe("2.67");
        PyNumber.Fixed(-0.001, 2).ShouldBe("-0.00");
        PyNumber.Thousands(1234567.891).ShouldBe("1,234,567.89");
        PyNumber.Thousands(-1234.5).ShouldBe("-1,234.50");
        PyNumber.Thousands(999.999).ShouldBe("1,000.00");
        PyNumber.WithSign(0).ShouldBe("+0.0");
        PyNumber.WithSign(-12.34).ShouldBe("-12.3");
        PyNumber.FixedComma(320.456).ShouldBe("320,46");
    }

    // ---------------------------------------------------------------- regras de agendamento (desvio 011)

    [Fact]
    [Trait("AC", "013-61")]
    public void SignedAmount_IsNegativeForExpensesAndPositiveForIncome()
    {
        ScheduleRules.SignedAmount("Despesa Essencial", 150m).ShouldBe(-150m);
        ScheduleRules.SignedAmount("Renda", 150m).ShouldBe(150m);
        ScheduleRules.SignedAmount("Renda", null).ShouldBe(0m);
        ScheduleRules.TransactionType("Renda").ShouldBe("Renda");
        ScheduleRules.TransactionType("Despesa").ShouldBe("Despesa");
    }

    [Theory]
    [Trait("AC", "013-62")]
    [InlineData(0, 3, 1, false)]
    [InlineData(1, 3, 2, false)]
    [InlineData(2, 3, 3, true)]
    [InlineData(3, 3, 4, true)]
    public void NextProgress_DeactivatesWithGreaterOrEqual(int executed, int total, int next, bool deactivate)
    {
        var progress = ScheduleRules.NextProgress(executed, total);

        progress.Executed.ShouldBe(next);
        progress.Deactivate.ShouldBe(deactivate);
    }

    [Fact]
    [Trait("AC", "013-62")]
    public void NextProgress_WithoutTotal_NeverDeactivates() => ScheduleRules.NextProgress(5, null).Deactivate.ShouldBeFalse();

    [Fact]
    [Trait("AC", "013-62")]
    public void InstallmentDescription_UsesNextNumberAndTotal()
    {
        ScheduleRules.InstallmentDescription("Curso", 2, 10).ShouldBe("Curso (3/10)");
        ScheduleRules.InstallmentDescription("Curso", 0, null).ShouldBe("Curso (1/None)");
        ScheduleRules.HasInstallmentLeft(9, 10).ShouldBeTrue();
        ScheduleRules.HasInstallmentLeft(10, 10).ShouldBeFalse();
        ScheduleRules.HasInstallmentLeft(50, null).ShouldBeTrue();
    }

    [Theory]
    [Trait("AC", "013-54")]
    [InlineData("2026-02-10", 31, "2026-02-28")]
    [InlineData("2028-02-10", 31, "2028-02-29")]
    [InlineData("2026-04-10", 31, "2026-04-30")]
    [InlineData("2026-04-10", 5, "2026-04-05")]
    public void DateInMonth_ClampsToTheLastDay(string reference, int day, string expected) =>
        ScheduleRules.DateInMonth(DateOnly.Parse(reference, System.Globalization.CultureInfo.InvariantCulture), day)
            .ShouldBe(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    [Trait("AC", "013-60")]
    public void MotorDayMatches_IsPlainEquality_SoDay31NeverFiresInApril()
    {
        ScheduleRules.MotorDayMatches(31, new DateOnly(2026, 4, 30)).ShouldBeFalse();
        ScheduleRules.MotorDayMatches(30, new DateOnly(2026, 4, 30)).ShouldBeTrue();
        ScheduleRules.MatchesMonth("ANUAL", 5, 5).ShouldBeTrue();
        ScheduleRules.MatchesMonth("ANUAL", 5, 6).ShouldBeFalse();
        ScheduleRules.MatchesMonth("MENSAL", null, 6).ShouldBeTrue();
    }

    [Theory]
    [Trait("AC", "013-63")]
    [InlineData("2026-03-07", 10, 3, "2026-03-10")]
    [InlineData("2026-03-10", 10, 0, "2026-03-10")]
    [InlineData("2026-03-28", 2, 5, "2026-04-02")]
    [InlineData("2026-03-26", 31, 5, "2026-03-31")]
    [InlineData("2026-04-25", 31, 5, "2026-04-30")]
    public void ReminderDueDate_FiresAtDueMinusNotifyDays(string today, int day, int notify, string expectedDue)
    {
        var due = ScheduleRules.ReminderDueDate(Schedule(ScheduleKinds.VariableReminder, day, notify: notify), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));

        due.ShouldBe(DateOnly.Parse(expectedDue, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("AC", "013-63")]
    public void ReminderDueDate_NotYet_OrWrongMonthForAnnual()
    {
        var today = new DateOnly(2026, 3, 6);

        ScheduleRules.ReminderDueDate(Schedule(ScheduleKinds.VariableReminder, 10, notify: 3), today).ShouldBeNull();
        ScheduleRules.ReminderDueDate(Schedule(ScheduleKinds.VariableReminder, 9, "ANUAL", month: 5, notify: 3), today).ShouldBeNull();
        ScheduleRules.ReminderDueDate(Schedule(ScheduleKinds.VariableReminder, 9, "ANUAL", month: 3, notify: 3), today).ShouldBe(new DateOnly(2026, 3, 9));
    }

    [Fact]
    [Trait("AC", "013-63")]
    public void ReminderMessage_IsLiteral_AndShowsQuestionMarksWithoutAmount()
    {
        ScheduleRules.ReminderMessage(Schedule(ScheduleKinds.VariableReminder, 10, notify: 3, amount: 1234.5m)).ShouldBe(
            "🔔 *LEMBRETE DE CONTA VARIÁVEL* 🔔\n\nSua conta 'Luz' vence em 3 dias (no dia 10).\n\nO valor previsto é: *R$ 1.234,50*\n\nPor favor, me diga o valor exato deste mês para eu registrar (ex: 'gastei 150.50 na conta de luz').");
        ScheduleRules.ReminderMessage(Schedule(amount: null)).ShouldContain("*???*");
        ScheduleRules.ReminderMessage(Schedule(amount: 0m)).ShouldContain("*???*");
    }

    // ---------------------------------------------------------------- check-in

    [Fact]
    [Trait("AC", "013-41")]
    public void CategorizeByDelay_UsesScheduleDayAndRollsOverTheMonth()
    {
        CheckinRules.CategorizeByDelay(18, new DateOnly(2026, 3, 18)).ShouldBe(("Vence hoje", 0));
        CheckinRules.CategorizeByDelay(17, new DateOnly(2026, 3, 18)).ShouldBe(("Venceu ontem", 1));
        CheckinRules.CategorizeByDelay(15, new DateOnly(2026, 3, 18)).ShouldBe(("Venceu dia 15", 3));
        CheckinRules.CategorizeByDelay(10, new DateOnly(2026, 3, 18)).ShouldBe((null, 8));
        CheckinRules.CategorizeByDelay(28, new DateOnly(2026, 3, 1)).ShouldBe(("Venceu ontem", 1));
    }

    [Fact]
    [Trait("AC", "013-41")]
    public void Deduplicate_RemovesOverdueThatAreAlreadyPending()
    {
        var a = Bill(1);
        var b = Bill(2);

        CheckinRules.Deduplicate([a], [a, b]).Select(x => x.Id).ShouldBe([2]);
    }

    private static CheckinBill Bill(int id) => new(
        id, "x", 1m, 10, 1, 1, 1, "c", "Conta Corrente", "cat", "macro", "Despesa Essencial", "FIXO", 0, null, null);

    // ---------------------------------------------------------------- relatório

    [Theory]
    [Trait("AC", "013-70")]
    [InlineData(MonthlyMoment.InicioMes, "2026-01-01", 12, 2025, "2025-12-01", "2025-12-31")]
    [InlineData(MonthlyMoment.InicioMes, "2026-03-01", 2, 2026, "2026-02-01", "2026-02-28")]
    [InlineData(MonthlyMoment.InicioMes, "2028-03-01", 2, 2028, "2028-02-01", "2028-02-29")]
    [InlineData(MonthlyMoment.FimMes, "2026-04-30", 4, 2026, "2026-04-01", "2026-04-30")]
    public void Period_InicioIsThePreviousMonth_FimIsTheCurrent(MonthlyMoment moment, string today, int month, int year, string start, string end)
    {
        var period = MonthlyReportRules.Period(moment, DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture));

        period.Month.ShouldBe(month);
        period.Year.ShouldBe(year);
        period.Start.ShouldBe(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture));
        period.End.ShouldBe(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("AC", "013-71")]
    public void ReportCalculations_KeepThePythonSignQuirks()
    {
        // Despesa é negativa no banco: saldo = rendas − despesas SOMA, e a variação percentual nunca aparece (anterior <= 0).
        MonthlyReportRules.Totals(-100, 1000, 3).PeriodBalance.ShouldBe(1100);
        MonthlyReportRules.Comparison(-200, -100).VariationPercent.ShouldBe(0);
        MonthlyReportRules.Comparison(200, 100).VariationPercent.ShouldBe(100);
        MonthlyReportRules.TopCategories([("A", -30), ("B", -10)]).Select(c => c.Percent).ShouldBe([75d, 25d]);
        MonthlyReportRules.TopCategories([("A", 0)]).Single().Percent.ShouldBe(0);
        MonthlyReportRules.Pot("Lazer", 0, 10).Percent.ShouldBe(0);
        MonthlyReportRules.Pot("Lazer", 100, -50).Percent.ShouldBe(-50);
        MonthlyReportRules.PreviousMonth(new DateOnly(2026, 1, 1)).ShouldBe((new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31)));
    }

    [Fact]
    [Trait("AC", "013-73")]
    public void Caption_UsesThePortugueseMonthName()
    {
        var data = new MonthlyReportData(
            3, 2026, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), new ReportTotals(0, 0, 0, 0), [],
            new ReportComparison(0, 0, 0, 0), [], new ReportAccounts(0, 0, 0, 0));

        MonthlyReportRules.Caption(data).ShouldBe("📊 Relatório de Março/2026");
    }

    // ---------------------------------------------------------------- contas a vencer

    [Fact]
    [Trait("AC", "013-32")]
    public void BillsReminder_BuildsThreeBlocksAndTotals()
    {
        var bills = new List<ReminderBill>
        {
            new("Aluguel", 1500, "Moradia", "Conta Corrente", "Despesa Essencial"),
            new("Salário", 5000, "Renda", "Conta Corrente", "Renda"),
            new("Streaming", 39.9, "Lazer", "Cartão de Crédito", "Despesa Discricionária"),
        };

        var text = BillsReminderFormatter.Format("Ana", bills, new DateOnly(2026, 3, 19));

        text.ShouldBe(
            "🔔 *Lembrete de Contas, Ana!*\n\n📅 Você tem *2 conta(s)* que vencem *amanhã* (19/03):\n\n" +
            "💸 *DESPESAS:*\n1. *Aluguel*\n   💰 R$ 1.500,00\n   📊 Moradia\n\n" +
            "💰 *RECEITAS:*\n1. *Salário*\n   💵 R$ 5.000,00\n   📊 Renda\n\n" +
            "💳 *CARTÃO (na fatura):*\nℹ️ Streaming\n   💰 R$ 39,90\n   📊 Lazer\n\n" +
            "━━━━━━━━━━━━━━━━\n💸 Total Despesas: R$ 1.500,00\n💰 Total Receitas: R$ 5.000,00\n💳 Total Cartão: R$ 39,90\n" +
            "\n_💳 Despesas do cartão são apenas lembretes (já debitadas na fatura)_");
        BillsReminderFormatter.Format("Ana", [], new DateOnly(2026, 3, 19)).ShouldBeNull();
        BillsReminderFormatter.Format("Ana", [bills[0]], new DateOnly(2026, 3, 19)).ShouldEndWith("\n_Não esqueça de pagar! 😊_");
    }
}
