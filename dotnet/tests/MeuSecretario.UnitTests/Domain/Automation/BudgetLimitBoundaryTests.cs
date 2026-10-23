using MeuSecretario.Domain.Automation;

namespace MeuSecretario.UnitTests.Domain.Automation;

/// <summary>
/// Desvio 260: gasto + valor que fecha exatamente o limite NÃO ultrapassa. Em <c>double</c>, R$ 0,14 + R$ 0,10 contra R$ 0,24 dava
/// "ultrapassará seu limite" (0,14 + 0,10 = 0,24000000000000002).
/// </summary>
public class BudgetLimitBoundaryTests
{
    private static BudgetCheck Check(double limit, double spent, double amount) =>
        BudgetValidation.Evaluate([(new PotRow(1, "Lazer", limit, "MENSAL"), spent)], amount);

    [Theory]
    [InlineData(0.24, 0.14, 0.10)]
    [InlineData(1.17, 0.07, 1.10)]
    [InlineData(0.58, 0.28, 0.30)]
    [InlineData(100.00, 33.10, 66.90)]
    [InlineData(250.30, 100.20, 150.10)]
    public void ReachingTheLimitExactly_DoesNotWarn(double limit, double spent, double amount)
    {
        var result = Check(limit, spent, amount);

        result.RequiresConfirmation.ShouldBeFalse();
        result.Message.ShouldBeEmpty();
        result.Checks.Single().Exceeds.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0.24, 0.14, 0.11)]
    [InlineData(100.00, 33.10, 66.91)]
    [InlineData(250.30, 100.20, 150.11)]
    public void OneCentOverTheLimit_Warns(double limit, double spent, double amount)
    {
        var result = Check(limit, spent, amount);

        result.RequiresConfirmation.ShouldBeTrue();
        result.Checks.Single().Exceeds.ShouldBeTrue();
        result.Message.ShouldContain("Excesso: R$ 0.01");
    }

    [Fact]
    public void EveryCentPairThatSumsExactlyToTheLimit_NeverWarns_AndOneCentMoreAlwaysWarns()
    {
        var falseAlarms = 0;
        var missed = 0;
        for (var spentCents = 0; spentCents <= 30_000; spentCents += 7)
        {
            foreach (var amountCents in new[] { 1, 10, 30, 110, 1990, 2500 })
            {
                var limitCents = spentCents + amountCents;
                if (Check(limitCents / 100d, spentCents / 100d, amountCents / 100d).RequiresConfirmation)
                {
                    falseAlarms++;
                }

                if (!Check(limitCents / 100d, spentCents / 100d, (amountCents + 1) / 100d).RequiresConfirmation)
                {
                    missed++;
                }
            }
        }

        (falseAlarms, missed).ShouldBe((0, 0));
    }

    [Fact]
    public void AmountOutsideTheMoneyRange_KeepsTheOldComparison()
    {
        // 1e15 não cabe em numeric(15,2): a comparação em decimal não se aplica, e a antiga continua valendo.
        Check(100, 50, 1e15).RequiresConfirmation.ShouldBeTrue();
    }
}
