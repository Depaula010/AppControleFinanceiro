using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.UnitTests.Domain.Transactions;

public class InvoiceCycleTests
{
    /// <summary>A tabela de 12 linhas da spec 004 (coluna "com P-004-1", aprovada no desvio 065).</summary>
    [Theory]
    [Trait("AC", "004-90")]
    [InlineData(10, 15, "2026-03-10", "2026-03-10", "2026-03-15")]
    [InlineData(10, 15, "2026-03-11", "2026-04-10", "2026-04-15")]
    [InlineData(10, 15, "2026-12-20", "2027-01-10", "2027-01-15")]
    [InlineData(25, 5, "2026-03-20", "2026-03-25", "2026-04-05")]
    [InlineData(25, 5, "2026-03-26", "2026-04-25", "2026-05-05")]
    [InlineData(10, 5, "2026-12-05", "2026-12-10", "2027-01-05")]
    [InlineData(31, 10, "2026-02-28", "2026-02-28", "2026-03-10")]
    [InlineData(31, 10, "2026-03-31", "2026-03-31", "2026-04-10")]
    [InlineData(30, 10, "2028-02-10", "2028-02-29", "2028-03-10")]
    [InlineData(5, 31, "2026-02-03", "2026-02-05", "2026-02-28")]
    [InlineData(5, 31, "2026-02-06", "2026-03-05", "2026-03-31")]
    [InlineData(10, 10, "2026-03-11", "2026-04-10", "2026-04-10")]
    public void ForPurchase_MatchesTheSpecTable(int closingDay, int dueDay, string date, string closing, string due)
    {
        var result = InvoiceCycle.ForPurchase(closingDay, dueDay, D(date));

        result.Closing.ShouldBe(D(closing));
        result.Due.ShouldBe(D(due));
    }

    [Fact]
    [Trait("AC", "004-91")]
    public void PurchaseOnTheClosingDay_StaysInThatInvoice_NextDayGoesToTheNext()
    {
        InvoiceCycle.ForPurchase(10, 20, new DateOnly(2026, 5, 10)).Closing.ShouldBe(new DateOnly(2026, 5, 10));
        InvoiceCycle.ForPurchase(10, 20, new DateOnly(2026, 5, 11)).Closing.ShouldBe(new DateOnly(2026, 6, 10));
    }

    [Fact]
    [Trait("AC", "004-92")]
    public void DueBeforeClosing_FallsOnTheMonthAfterTheClosing()
    {
        InvoiceCycle.ForPurchase(25, 5, new DateOnly(2026, 3, 20)).Due.ShouldBe(new DateOnly(2026, 4, 5));
        InvoiceCycle.ForPurchase(10, 5, new DateOnly(2026, 12, 5)).Due.ShouldBe(new DateOnly(2027, 1, 5));
    }

    /// <summary>
    /// A fórmula literal do Flask (invoice_service.py:27-62) como referência: com o desvio 065 ela só diverge quando
    /// a compra é depois do fechamento (D &gt; F) e V ≥ F; todo o resto é igual (~200 combinações por dia e mês).
    /// </summary>
    [Fact]
    [Trait("AC", "004-90")]
    public void AgainstTheLiteralFlaskFormula_OnlyDiffersWhenPurchaseIsAfterClosingAndDueIsNotBeforeClosing()
    {
        var checkedCases = 0;
        foreach (var (closingDay, dueDay) in new[] { (10, 15), (25, 5), (31, 10), (5, 31), (30, 28), (1, 1), (28, 28), (15, 10) })
        {
            for (var date = new DateOnly(2027, 12, 20); date <= new DateOnly(2028, 3, 5); date = date.AddDays(1))
            {
                var literal = LiteralFlask(closingDay, dueDay, date);
                var current = InvoiceCycle.ForPurchase(closingDay, dueDay, date);

                var closingThisMonth = MonthDays.Clamp(date.Year, date.Month, closingDay);
                var afterClosing = date > closingThisMonth;

                current.Closing.ShouldBe(literal.Closing, $"F={closingDay} V={dueDay} D={date}");
                if (afterClosing && dueDay >= closingDay)
                {
                    current.Due.ShouldBeLessThan(literal.Due, $"F={closingDay} V={dueDay} D={date}");
                    var oneMonthEarlier = new DateOnly(literal.Due.Year, literal.Due.Month, 1).AddMonths(-1);
                    current.Due.ShouldBe(MonthDays.Clamp(oneMonthEarlier.Year, oneMonthEarlier.Month, dueDay));
                }
                else
                {
                    current.Due.ShouldBe(literal.Due, $"F={closingDay} V={dueDay} D={date}");
                }
                checkedCases++;
            }
        }

        checkedCases.ShouldBeGreaterThan(200);
    }

    private static DateOnly D(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static InvoiceDates LiteralFlask(int closingDay, int dueDay, DateOnly date)
    {
        var closingThisMonth = MonthDays.Clamp(date.Year, date.Month, closingDay);
        if (date <= closingThisMonth)
        {
            var due = dueDay < closingDay ? MonthDays.Clamp(Next(date).Year, Next(date).Month, dueDay) : MonthDays.Clamp(date.Year, date.Month, dueDay);
            return new InvoiceDates(closingThisMonth, due);
        }

        var closingMonth = Next(date);
        var dueMonth = Next(closingMonth);
        return new InvoiceDates(
            MonthDays.Clamp(closingMonth.Year, closingMonth.Month, closingDay),
            MonthDays.Clamp(dueMonth.Year, dueMonth.Month, dueDay));
    }

    private static DateOnly Next(DateOnly date) => new DateOnly(date.Year, date.Month, 1).AddMonths(1);
}
