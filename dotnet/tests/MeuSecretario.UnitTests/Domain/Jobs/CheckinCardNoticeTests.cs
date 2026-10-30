using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.UnitTests.Domain.Jobs;

/// <summary>Desvio 267: o check-in noturno avisa as contas do cartão cobradas hoje, só como informação.</summary>
public class CheckinCardNoticeTests
{
    private static readonly DateOnly Today = new(2026, 10, 17);

    private static readonly CheckinBill Rent = new(
        1, "Aluguel", 1500m, 17, 1, 10, 7, "Nubank", "Conta Corrente", "Moradia", "Moradia", "Despesa", "FIXO", null, null, Today);

    [Fact]
    [Trait("AC", "013-45")]
    public void CardBillAlreadyLaunched_StillShowsAsInformation_AndIsNotNumbered()
    {
        var snapshot = new CheckinSnapshot([Rent], [], [], [], [new CardBillNotice("Claude AI", 110m, "Cartão Inter")]);

        var message = CheckinMessageFormatter.Format(snapshot, Today, "abcd1234");

        message.ShouldNotBeNull();
        message.ShouldContain("💳 *DÉBITO CARTÃO DE CRÉDITO*\n * Claude AI - R$ 110,00 - Será debitado hoje\n");
        message.ShouldContain("1. Aluguel - R$ 1.500,00");
        message.ShouldNotContain("2. Claude");
    }

    [Fact]
    [Trait("AC", "013-45")]
    public void OnlyACardNotice_StillProducesTheMessage_WithoutHowToAnswer()
    {
        var snapshot = new CheckinSnapshot([], [], [], [], [new CardBillNotice("Claude AI", 110m, "Cartão Inter")]);

        snapshot.IsEmpty.ShouldBeFalse();
        var message = CheckinMessageFormatter.Format(snapshot, Today, "abcd1234");

        message.ShouldNotBeNull();
        message.ShouldContain("Claude AI");
        message.ShouldNotContain("COMO RESPONDER");
    }

    [Fact]
    [Trait("AC", "013-45")]
    public void WithoutTheCardList_ItFallsBackToThePendingCardBills_AsBefore()
    {
        var pendingCard = new CheckinBill(
            2, "Gympass", 183.2m, 17, 3, 10, 7, "Cartão Inter", "Cartão de Crédito", "Lazer", "Lazer", "Despesa", "FIXO", null, null, Today);

        var message = CheckinMessageFormatter.Format(new CheckinSnapshot([pendingCard], [], [], []), Today, "abcd1234");

        message.ShouldNotBeNull();
        message.ShouldContain("Gympass - R$ 183,20 - Será debitado hoje");
    }
}
