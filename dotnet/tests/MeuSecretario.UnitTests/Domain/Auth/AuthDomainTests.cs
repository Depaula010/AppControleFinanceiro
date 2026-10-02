using System.Numerics;
using MeuSecretario.Domain.Auth;

namespace MeuSecretario.UnitTests.Domain.Auth;

public class PhoneNumberTests
{
    [Theory]
    [Trait("AC", "001-02")]
    [InlineData("+55 (11) 99999-9999", "5511999999999")]
    [InlineData("5511999999999", "5511999999999")]
    [InlineData("11 9 9999.9999", "11999999999")]
    [InlineData("abc", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Digits_KeepsOnlyAsciiDigits(string? raw, string expected)
    {
        PhoneNumber.Digits(raw).ShouldBe(expected);
    }
}

public class PasswordRulesTests
{
    [Theory]
    [Trait("AC", "001-04")]
    [InlineData("12345", false)]
    [InlineData("123456", true)]
    [InlineData("abc", false)]
    [InlineData("SenhaSegura123", true)]
    [InlineData("ãéíõü", false)]     // 5 caracteres
    [InlineData("ãéíõüç", true)]     // 6 caracteres
    [InlineData("🔑🔑🔑🔑🔑", false)] // 5 caracteres (10 unidades UTF-16): conta como o len() do Python
    [InlineData("🔑🔑🔑🔑🔑🔑", true)]
    public void IsValid_RequiresAtLeastSixCharacters(string password, bool expected)
    {
        PasswordRules.IsValid(password).ShouldBe(expected);
    }
}

public class CardDaysTests
{
    [Theory]
    [Trait("AC", "001-07")]
    [InlineData(10, 5)]
    [InlineData("10", "5")]
    [InlineData(" 10 ", "05")]       // int(" 10 ") do Python
    [InlineData(10.9, 5.1)]          // int() trunca
    [InlineData(1, 31)]
    public void Parse_AcceptsIntegersNumericStringsAndTruncatesDecimals(object venc, object fech)
    {
        var result = CardDays.Parse(venc, fech);

        result.Outcome.ShouldBe(CardDaysOutcome.Ok);
    }

    [Fact]
    [Trait("AC", "001-07")]
    public void Parse_TruncatesDecimalsLikePythonInt()
    {
        var result = CardDays.Parse(10.9, 5.1);

        result.Vencimento.ShouldBe(10);
        result.Fechamento.ShouldBe(5);
    }

    [Theory]
    [Trait("AC", "001-06")]
    [InlineData(0, 5, "dia_vencimento")]
    [InlineData(32, 5, "dia_vencimento")]
    [InlineData(-1, 5, "dia_vencimento")]
    [InlineData(10, 0, "dia_fechamento")]
    [InlineData(10, 32, "dia_fechamento")]
    [InlineData(0, 40, "dia_vencimento")] // ambos inválidos: vale o do vencimento
    public void Parse_ReportsTheFirstOutOfRangeField(int venc, int fech, string expectedField)
    {
        var result = CardDays.Parse(venc, fech);

        result.Outcome.ShouldBe(CardDaysOutcome.OutOfRange);
        result.Field.ShouldBe(expectedField);
    }

    [Fact]
    [Trait("AC", "001-06")]
    public void Parse_HugeIntegerIsOutOfRange_NotAFormatError()
    {
        // Python: int(10**30) funciona e cai na checagem de faixa.
        CardDays.Parse(BigInteger.Pow(10, 30), 5).Outcome.ShouldBe(CardDaysOutcome.OutOfRange);
    }

    [Theory]
    [Trait("AC", "001-07")]
    [InlineData("abc")]
    [InlineData("10.5")]
    [InlineData("")]
    [InlineData(true)]
    [InlineData(false)]
    public void Parse_RejectsValuesThatAreNotIntegers(object bad)
    {
        CardDays.Parse(bad, 5).Outcome.ShouldBe(CardDaysOutcome.NotInteger);
        CardDays.Parse(10, bad).Outcome.ShouldBe(CardDaysOutcome.NotInteger);
    }

    [Fact]
    [Trait("AC", "001-07")]
    public void Parse_FormatErrorWinsOverRangeError_BecauseBothAreConvertedFirst()
    {
        // Python converte as duas com int() antes de checar a faixa.
        CardDays.Parse(0, "abc").Outcome.ShouldBe(CardDaysOutcome.NotInteger);
    }

    [Fact]
    public void Parse_RejectsObjectsAndArrays()
    {
        CardDays.Parse(new object(), 5).Outcome.ShouldBe(CardDaysOutcome.NotInteger);
    }
}

public class DefaultAccountsTests
{
    [Fact]
    public void For_ReturnsTheThreeDefaultAccountsWithLiteralNamesAndTypes()
    {
        var accounts = DefaultAccounts.For(10, 5);

        accounts.Count.ShouldBe(3);
        accounts[0].ShouldBe(new NewAccount("Carteira", "Dinheiro", null, null));
        accounts[1].ShouldBe(new NewAccount("Conta Corrente", "Conta Corrente", null, null));
        accounts[2].ShouldBe(new NewAccount("Cartão de Crédito", "Cartão de Crédito", 10, 5));
    }

    [Fact]
    public void For_OnlyTheCreditCardCarriesDays()
    {
        // CHECK chk_cartao_credito do banco: dias só existem se o tipo for cartão.
        var accounts = DefaultAccounts.For(1, 31);

        accounts.Where(a => a.TipoConta != "Cartão de Crédito")
            .ShouldAllBe(a => a.DiaVencimento == null && a.DiaFechamento == null);
    }
}
