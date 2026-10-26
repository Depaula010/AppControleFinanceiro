using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Desvio 265: só a mensagem inteira pedindo o menu conta.</summary>
public class HelpCommandTests
{
    [Theory]
    [InlineData("menu")]
    [InlineData("Menu")]
    [InlineData("  MENU  ")]
    [InlineData("menu?")]
    [InlineData("/menu")]
    [InlineData("ajuda")]
    [InlineData("Ajuda!")]
    [InlineData("help")]
    [InlineData("menu de ajuda")]
    [InlineData("Menu  de   Ajuda")]
    [InlineData("comandos")]
    [InlineData("lista de comandos")]
    [InlineData("o que você faz?")]
    public void WholeMessageAskingForTheMenu_IsAHelpRequest(string message)
    {
        HelpCommand.IsHelpRequest(message).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ajuda com a fatura")]
    [InlineData("qual o menu do dia")]
    [InlineData("menu da fatura do inter")]
    [InlineData("gastei 21 reais na padaria")]
    [InlineData("socorro")]
    [InlineData("ok")]
    public void AnythingElse_IsNot(string message)
    {
        HelpCommand.IsHelpRequest(message).ShouldBeFalse();
    }

    [Fact]
    public void Null_IsNot()
    {
        HelpCommand.IsHelpRequest(null).ShouldBeFalse();
    }
}
