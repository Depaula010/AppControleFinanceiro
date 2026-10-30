using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.UnitTests.Domain.Jobs;

/// <summary>Desvio 267: conta fixa do cartão aparece nos alertas da manhã só como informação (não é "vence hoje" a pagar).</summary>
public class FinancialAlertCardTests
{
    private static AlertBill Bill(string description, double value, bool onCard = false, string type = AlertBill.Expense) =>
        new(1, description, value, "Categoria", onCard ? "Cartão Inter" : "Conta", type, onCard);

    [Fact]
    [Trait("AC", "013-32")]
    public void CardBills_GoToTheirOwnInformativeBlock_NotToTheDueSections()
    {
        var alerts = new FinancialAlerts(
            [Bill("Aluguel", 1500), Bill("Gympass", 183.2, onCard: true)],
            [Bill("Claude AI", 110, onCard: true)],
            [], []);

        var message = FinancialAlertFormatter.Format(alerts, includeGreeting: false);

        message.ShouldNotBeNull();
        message.ShouldContain("⚠️ *VENCE HOJE (Despesas):*\n• Aluguel - R$ 1500,00");
        message.ShouldContain("💳 *NA FATURA DO CARTÃO (só informativo):*\n• Hoje: Gympass - R$ 183,20\n• Amanhã: Claude AI - R$ 110,00");
        message.ShouldNotContain("• Gympass - R$ 183,20");
        message.ShouldNotContain("VENCE AMANHÃ");
    }

    [Fact]
    [Trait("AC", "013-32")]
    public void OnlyCardBills_StillProduceTheMessage_WithoutAnEmptyDueSection()
    {
        var message = FinancialAlertFormatter.Format(new FinancialAlerts([Bill("Gympass", 183.2, onCard: true)], [], [], []), includeGreeting: true);

        message.ShouldNotBeNull();
        message.ShouldStartWith("🌅 *Bom dia!*\n\n💰 *ALERTAS FINANCEIROS*");
        message.ShouldNotContain("VENCE HOJE");
        message.ShouldContain("• Hoje: Gympass - R$ 183,20");
    }

    [Fact]
    [Trait("AC", "013-32")]
    public void WithoutCardBills_TheTextIsTheSameAsBefore()
    {
        var message = FinancialAlertFormatter.Format(new FinancialAlerts([Bill("Aluguel", 1500)], [], [], []), includeGreeting: false);

        message.ShouldBe("💰 *ALERTAS FINANCEIROS*\n\n⚠️ *VENCE HOJE (Despesas):*\n• Aluguel - R$ 1500,00");
        message!.ShouldNotContain("CARTÃO");
    }
}
