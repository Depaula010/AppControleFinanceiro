using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.UnitTests.Domain.Accounts;

public class AccountNameTests
{
    [Fact]
    [Trait("AC", "002-16")]
    public void Normalize_TrimsTheEnds()
    {
        AccountName.Normalize("  Nubank \t").ShouldBe("Nubank");
    }

    [Fact]
    [Trait("AC", "002-16")]
    public void Normalize_CutsAt100CodePoints_NotUtf16Units()
    {
        var raw = new string('á', 99) + "😀" + "extra";   // 99 + emoji (2 unidades UTF-16) = 100 pontos de código

        var name = AccountName.Normalize(raw);

        name.EnumerateRunes().Count().ShouldBe(100);
        name.ShouldEndWith("😀");
        name.ShouldNotContain("extra");
    }

    [Fact]
    public void Truncate_KeepsShortNamesIntact()
    {
        AccountName.Truncate("Carteira").ShouldBe("Carteira");
        AccountName.Truncate(new string('x', 100)).Length.ShouldBe(100);
        AccountName.Truncate(new string('x', 101)).Length.ShouldBe(100);
    }

    [Fact]
    [Trait("AC", "002-43")]
    public void Truncate_DoesNotTrimAgainAfterTheCut()
    {
        AccountName.Truncate(new string('x', 99) + " yyy").ShouldBe(new string('x', 99) + " ");
    }
}

public class AccountTypesTests
{
    [Theory]
    [Trait("AC", "002-15")]
    [InlineData("Conta Corrente", true)]
    [InlineData("Conta Poupança", true)]
    [InlineData("Investimento", true)]
    [InlineData("Cartão de Crédito", true)]
    [InlineData("Dinheiro", true)]
    [InlineData("Outro", true)]
    [InlineData("corrente", false)]
    [InlineData("conta corrente", false)]
    [InlineData("Conta  Corrente", false)]
    [InlineData("Cartao de Credito", false)]
    [InlineData("", false)]
    public void IsValid_IsExact(string value, bool expected)
    {
        AccountTypes.IsValid(value).ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "002-15")]
    public void All_HasTheSixTypesInTheMessageOrder()
    {
        string.Join(", ", AccountTypes.All).ShouldBe(
            "Conta Corrente, Conta Poupança, Investimento, Cartão de Crédito, Dinheiro, Outro");
        AccountTypes.Card.ShouldBe("Cartão de Crédito");
    }
}
