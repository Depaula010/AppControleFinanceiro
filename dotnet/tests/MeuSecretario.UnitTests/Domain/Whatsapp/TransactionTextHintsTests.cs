using System.Globalization;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Desvio 281: data e parcelas lidas do texto. Hoje nos testes é segunda-feira, 16/11/2026.</summary>
public class TransactionTextHintsTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);

    private static DateOnly? DateOf(string text) => TransactionTextHints.Parse(text, Today).Date;

    [Theory]
    [Trait("AC", "012-125")]
    [InlineData("gastei 80 ontem", "2026-11-15")]
    [InlineData("gastei 80 anteontem no mercado", "2026-11-14")]
    [InlineData("gastei 80 hoje", "2026-11-16")]
    [InlineData("gastei 80 dia 10", "2026-11-10")]
    [InlineData("gastei 80 no dia 20", "2026-10-20")]
    [InlineData("gastei 80 dia 31", "2026-10-31")]
    [InlineData("gastei 80 em 10/11", "2026-11-10")]
    [InlineData("gastei 80 em 20/11", "2025-11-20")]
    [InlineData("gastei 80 em 10/11/2026", "2026-11-10")]
    [InlineData("gastei 80 em 05/03/26", "2026-03-05")]
    [InlineData("gastei 80 dia 5 de março", "2026-03-05")]
    [InlineData("gastei 80 em 10 de novembro", "2026-11-10")]
    [InlineData("gastei 80 na sexta", "2026-11-13")]
    [InlineData("gastei 80 segunda passada", "2026-11-09")]
    [InlineData("gastei 80 no sábado", "2026-11-14")]
    public void ReadsTheDate(string text, string expected) =>
        DateOf(text).ShouldBe(DateOnly.Parse(expected, CultureInfo.InvariantCulture));

    [Theory]
    [Trait("AC", "012-125")]
    [InlineData("gastei 50 na padaria")]
    [InlineData("paguei a quarta parcela")]
    [InlineData("gastei 80 em 31/02")]
    [InlineData("comprei 2x1 de cerveja")]
    [InlineData("")]
    public void IgnoresWhatIsNotADate(string text) => DateOf(text).ShouldBeNull();

    [Theory]
    [Trait("AC", "012-125")]
    [InlineData("comprei tv 3000 em 10x", 10)]
    [InlineData("comprei tv 3000 10x", 10)]
    [InlineData("comprei tv 3000 em 12 vezes", 12)]
    [InlineData("comprei tv 3000 em 6 parcelas", 6)]
    [InlineData("comprei tv 3000 parcelado em 5", 5)]
    [InlineData("comprei tv em 3 parcelas de 100", 3)]
    public void ReadsTheInstallmentCount(string text, int expected) =>
        TransactionTextHints.Parse(text, Today).Installments.ShouldBe(expected);

    [Theory]
    [Trait("AC", "012-125")]
    [InlineData("gastei 3000 à vista")]
    [InlineData("comprei 1x")]
    [InlineData("paguei 3 contas")]
    public void NoInstallments(string text) => TransactionTextHints.Parse(text, Today).Installments.ShouldBeNull();

    [Theory]
    [Trait("AC", "012-125")]
    [InlineData("comprei tv em 10x de 300", 10, "300")]
    [InlineData("comprei tv 3x de 1.234,56", 3, "1234.56")]
    [InlineData("comprei tv 4 vezes de R$ 89,9", 4, "89.90")]
    public void ReadsTheValueOfEachInstallment(string text, int count, string each)
    {
        var hints = TransactionTextHints.Parse(text, Today);

        hints.Installments.ShouldBe(count);
        hints.InstallmentValue.ShouldBe(decimal.Parse(each, CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("AC", "012-125")]
    public void CleanText_DropsTheDateAndInstallmentPhrases_SoTheyAreNotTakenAsAnAccountName()
    {
        var hints = TransactionTextHints.Parse("gastei 50 ontem em 3 vezes no nubank", Today);

        hints.Date.ShouldBe(new DateOnly(2026, 11, 15));
        hints.Installments.ShouldBe(3);
        hints.CleanText.ShouldBe("gastei 50 no nubank");
    }
}
