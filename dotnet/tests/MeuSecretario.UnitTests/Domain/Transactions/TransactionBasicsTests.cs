using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.UnitTests.Domain.Transactions;

public class TransactionTypesTests
{
    [Theory]
    [Trait("AC", "004-04")]
    [InlineData("Renda", "Receita")]
    [InlineData("Despesa", "Despesa")]
    [InlineData("Transferência", "Transferência")]
    [InlineData("Pagamento Fatura", "Pagamento Fatura")]
    public void ToApi_OnlyRendaBecomesReceita(string db, string api)
    {
        TransactionTypes.ToApi(db).ShouldBe(api);
    }

    [Theory]
    [Trait("AC", "004-17")]
    [InlineData("Receita", TransactionKind.Income)]
    [InlineData("Despesa", TransactionKind.Expense)]
    public void FromApi_AcceptsOnlyTheTwoExactTexts(string api, TransactionKind kind)
    {
        TransactionTypes.FromApi(api).ShouldBe(kind);
    }

    [Theory]
    [Trait("AC", "004-17")]
    [InlineData("receita")]
    [InlineData("Renda")]
    [InlineData("Transferência")]
    [InlineData("")]
    [InlineData(null)]
    public void FromApi_RejectsEverythingElse(string? api)
    {
        TransactionTypes.FromApi(api).ShouldBeNull();
    }

    [Fact]
    public void ToDb_MapsKinds()
    {
        TransactionTypes.ToDb(TransactionKind.Income).ShouldBe("Renda");
        TransactionTypes.ToDb(TransactionKind.Expense).ShouldBe("Despesa");
    }

    [Theory]
    [Trait("AC", "004-85")]
    [InlineData("Renda", true)]
    [InlineData("Despesa", true)]
    [InlineData("Transferência", false)]
    [InlineData("Pagamento Fatura", false)]
    public void IsEditable_OnlyIncomeAndExpense(string db, bool editable)
    {
        TransactionTypes.IsEditable(db).ShouldBe(editable);
    }
}

public class SignedAmountTests
{
    [Fact]
    [Trait("AC", "004-10")]
    public void Apply_IncomePositive_ExpenseNegative()
    {
        SignedAmount.Apply(TransactionKind.Income, 3000m).ShouldBe(3000m);
        SignedAmount.Apply(TransactionKind.Expense, 150.5m).ShouldBe(-150.5m);
        SignedAmount.Apply(TransactionKind.Expense, 0m).ShouldBe(0m);
    }

    [Theory]
    [Trait("AC", "004-43")]
    [InlineData("Despesa", null, 180, -180)]
    [InlineData("Renda", null, 180, 180)]
    [InlineData("Despesa", TransactionKind.Income, 10, 10)]
    [InlineData("Renda", TransactionKind.Expense, 50, -50)]
    [InlineData("Renda", TransactionKind.Income, 50, 50)]
    [InlineData("Transferência", null, 5, -5)]
    public void ForUpdate_BodyTypeWinsOverCurrentType(string current, TransactionKind? body, int magnitude, int expected)
    {
        SignedAmount.ForUpdate(current, body, magnitude).ShouldBe(expected);
    }
}

public class FlaskDateTests
{
    [Theory]
    [Trait("AC", "004-06")]
    [InlineData("2025-12-11", 2025, 12, 11)]
    [InlineData("2026-1-5", 2026, 1, 5)]
    [InlineData("2028-02-29", 2028, 2, 29)]
    [InlineData("0001-01-01", 1, 1, 1)]
    public void Valid_Parses(string text, int year, int month, int day)
    {
        FlaskDate.TryParse(text, out var date).ShouldBeTrue();
        date.ShouldBe(new DateOnly(year, month, day));
    }

    [Theory]
    [Trait("AC", "004-06")]
    [InlineData("2026-02-30")]
    [InlineData("2027-02-29")]
    [InlineData("2025-13-01")]
    [InlineData("2025-00-10")]
    [InlineData("2025-10-00")]
    [InlineData("05/01/2026")]
    [InlineData("2026-12-11T10:00")]
    [InlineData(" 2026-12-11")]
    [InlineData("2026-12-11 ")]
    [InlineData("26-12-11")]
    [InlineData("2026-123-1")]
    [InlineData("0000-01-01")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_DoesNotParse(string? text)
    {
        FlaskDate.TryParse(text, out _).ShouldBeFalse();
    }
}

public class MonthDaysTests
{
    [Theory]
    [InlineData(2026, 2, 31, 28)]
    [InlineData(2028, 2, 30, 29)]
    [InlineData(2026, 4, 31, 30)]
    [InlineData(2026, 1, 31, 31)]
    public void Clamp_FallsOnTheLastDay(int year, int month, int day, int expectedDay)
    {
        MonthDays.Clamp(year, month, day).ShouldBe(new DateOnly(year, month, expectedDay));
    }

    [Fact]
    [Trait("AC", "004-134")]
    public void AddMonths_ClampsLikeRelativedelta()
    {
        MonthDays.AddMonths(new DateOnly(2026, 1, 31), 1).ShouldBe(new DateOnly(2026, 2, 28));
        MonthDays.AddMonths(new DateOnly(2027, 12, 29), 1).ShouldBe(new DateOnly(2028, 1, 29));
    }
}

public class PostingDescriptionsTests
{
    [Fact]
    [Trait("AC", "004-115")]
    public void Texts_AreLiteral()
    {
        PostingDescriptions.TransferOut("Conta Corrente").ShouldBe("Transferência para Conta Corrente");
        PostingDescriptions.TransferIn("Carteira").ShouldBe("Transferência de Carteira");
        PostingDescriptions.PaymentOut("Nubank").ShouldBe("Pagamento Fatura Nubank");
        PostingDescriptions.PaymentIn("Itaú").ShouldBe("Pagamento Recebido (de Itaú)");
    }
}

public class InstallmentScheduleTests
{
    [Theory]
    [Trait("AC", "004-134")]
    [InlineData(2026, 1, 31, 2026, 2, 28, 31)]
    [InlineData(2026, 3, 15, 2026, 4, 15, 15)]
    [InlineData(2027, 12, 29, 2028, 1, 29, 29)]
    public void For_StartsNextMonthOnTheSameDay(int y, int m, int d, int sy, int sm, int sd, int day)
    {
        var plan = InstallmentSchedule.For(new DateOnly(y, m, d), 33.33m, 3);

        plan.StartDate.ShouldBe(new DateOnly(sy, sm, sd));
        plan.DayOfMonth.ShouldBe(day);
        plan.InstallmentsExecuted.ShouldBe(1);
        plan.Total.ShouldBe(3);
        plan.Amount.ShouldBe(33.33m);
    }
}
