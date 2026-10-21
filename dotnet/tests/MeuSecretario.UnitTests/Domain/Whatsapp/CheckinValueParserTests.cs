using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Resposta ao valor de um lembrete variável no check-in (desvio 252).</summary>
public class CheckinValueParserTests
{
    [Theory]
    [InlineData("150", 150)]
    [InlineData("150,50", 150.50)]
    [InlineData("150.50", 150.50)]
    [InlineData("R$ 44,27", 44.27)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1.234", 1234)]
    [InlineData("1234,5", 1234.5)]
    [InlineData("foi 89 reais", 89)]
    public void Amount_AcceptsCommaOrDotAndThousands(string message, double expected)
    {
        var reply = CheckinValueParser.Parse(message);

        reply.Kind.ShouldBe(CheckinValueKind.Amount);
        reply.Amount.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("Sim")]
    [InlineData("👍")]
    [InlineData("o previsto")]
    public void Confirmation_UsesTheExpectedAmount(string message) =>
        CheckinValueParser.Parse(message).Kind.ShouldBe(CheckinValueKind.UseExpected);

    [Theory]
    [InlineData("pular")]
    [InlineData("Não")]
    [InlineData("depois")]
    [InlineData("ok não")]
    public void Skip_HasPriorityOverConfirmation(string message) =>
        CheckinValueParser.Parse(message).Kind.ShouldBe(CheckinValueKind.Skip);

    [Theory]
    [InlineData("")]
    [InlineData("sei lá")]
    [InlineData("0")]
    [InlineData("0,00")]
    [InlineData("1,2345")]
    [InlineData("999999999")]
    public void Unrecognized_OrOutOfRange_IsInvalid(string message) =>
        CheckinValueParser.Parse(message).Kind.ShouldBe(CheckinValueKind.Invalid);
}
