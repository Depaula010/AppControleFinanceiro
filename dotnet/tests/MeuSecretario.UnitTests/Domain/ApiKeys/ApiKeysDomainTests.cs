using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.UnitTests.Domain.ApiKeys;

public class ApiKeysDomainTests
{
    [Fact]
    public void PreferenceKind_Labels()
    {
        PreferenceKind.Label(true).ShouldBe("própria (grátis)");
        PreferenceKind.Label(false).ShouldBe("sistema (cobrado)");
    }

    private static UsageRow Row(string provider, string keyType, long total) => new(provider, keyType, total, "2026-10", null);

    [Fact]
    [Trait("AC", "008-30")]
    public void UsageSummary_SumsByKeyType_AndIgnoresOtherTypes()
    {
        var summary = UsageSummary.From([
            Row("gemini", "propria", 3),
            Row("gemini", "sistema", 4),
            Row("weather", "sistema", 5),
            Row("weather", "outro", 100),
        ]);

        summary.OwnKeyCalls.ShouldBe(3);
        summary.SystemKeyCalls.ShouldBe(9);
        summary.TotalCalls.ShouldBe(12);
    }

    [Fact]
    [Trait("AC", "008-30")]
    public void UsageSummary_Empty_IsZero()
    {
        UsageSummary.From([]).ShouldBe(new UsageSummary(0, 0, 0));
    }

    [Theory]
    [Trait("AC", "008-66")]
    [InlineData(100, 99, false)]
    [InlineData(100, 100, true)]
    [InlineData(100, 101, true)]
    [InlineData(0, 0, true)]
    [InlineData(null, 1_000_000, false)]
    public void PlanLimit_Exceeded_UsesGreaterOrEqual(int? limit, long used, bool expected)
    {
        PlanLimit.Exceeded(limit, used).ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "008-30")]
    public void UsageMonth_UsesTheLocalDate_AtTheMonthTurn()
    {
        // 31/10 23:30 em São Paulo ainda é outubro, embora em UTC já seja novembro.
        UsageMonth.Of(new DateTime(2026, 10, 31, 23, 30, 0)).ShouldBe("2026-10");
        UsageMonth.Of(new DateTime(2026, 11, 1, 0, 0, 0)).ShouldBe("2026-11");
        UsageMonth.Of(new DateTime(2026, 1, 5)).ShouldBe("2026-01");
    }

    [Theory]
    [Trait("AC", "008-48")]
    [InlineData("CONFIRMO_DELECAO", true)]
    [InlineData("confirmo_delecao", false)]
    [InlineData(" CONFIRMO_DELECAO", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AccountDeletion_RequiresTheExactConfirmation(string? value, bool expected)
    {
        AccountDeletion.IsConfirmed(value).ShouldBe(expected);
    }
}
