using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Desvio 283: o saldo do bot só ganha a nota "projetado com faturas futuras" quando difere do saldo de hoje.</summary>
public class BalanceProjectedTests
{
    [Fact]
    [Trait("AC", "012-301")]
    public void SingleAccount_ShowsTheNote_OnlyWhenProjectedDiffers()
    {
        var same = BalanceMessages.Format([new AccountBalance("Nubank", "Conta Corrente", 100, 100)]);
        var different = BalanceMessages.Format([new AccountBalance("Nubank", "Conta Corrente", 100, -1234.5)]);
        var legacy = BalanceMessages.Format([new AccountBalance("Nubank", "Conta Corrente", 100)]);

        same.ShouldNotContain("projetado");
        legacy.ShouldNotContain("projetado");
        different.ShouldEndWith("💵 Saldo: *R$ 100,00*\n_(projetado com faturas futuras: R$ -1.234,50)_");
    }

    [Fact]
    [Trait("AC", "012-301")]
    public void SeveralAccounts_ShowTheProjectedTotalAfterTheTotal()
    {
        var text = BalanceMessages.Format(
        [
            new AccountBalance("Nubank", "Conta Corrente", 1000, 1000),
            new AccountBalance("Inter", "Cartão de Crédito", -200, -700.25),
        ]);

        text.ShouldEndWith("💵 *Total Geral:* R$ 800,00\n_(projetado com faturas futuras: R$ 299,75)_");
        BalanceMessages.Format([new AccountBalance("A", "Dinheiro", 5, 5), new AccountBalance("B", "Dinheiro", 5, 5)]).ShouldNotContain("projetado");
    }
}
