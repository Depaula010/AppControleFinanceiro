using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.UnitTests.Domain.Transactions;

public class InstallmentSplitTests
{
    [Theory]
    [InlineData(100, 3)]
    [InlineData(200, 3)]
    [InlineData(10, 7)]
    [InlineData(1000, 12)]
    [InlineData(0.05, 3)]
    [InlineData(99.99, 6)]
    public void TheInstallmentsAlwaysAddUpToTheTotal(double total, int count)
    {
        // O valor chega da conversa como total/N em double, como em BuildPending.
        var exact = (decimal)(total / count);
        var (per, first) = InstallmentSplit.Split(exact, count);

        (first + (per * (count - 1))).ShouldBe(Math.Round((decimal)total, 2));
        first.ShouldBeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void ThirdsOfOneHundred_PutTheLeftoverCentOnTheFirst()
    {
        var (per, first) = InstallmentSplit.Split(33.333333333333336m, 3);

        per.ShouldBe(33.33m);
        first.ShouldBe(33.34m);
    }

    [Fact]
    public void ThirdsOfTwoHundred_TakeTheExtraCentOffTheFirst()
    {
        var (per, first) = InstallmentSplit.Split(66.66666666666667m, 3);

        per.ShouldBe(66.67m);
        first.ShouldBe(66.66m);
    }
}
