using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Jobs;

/// <summary>Desvio 264: a fatura de cartão é um item numerado do check-in e a resposta a escolhe como as contas.</summary>
public class CheckinInvoiceMessageTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    private static readonly CheckinBill Rent = new(
        1, "Aluguel", 1500m, 25, 1, 10, 7, "Nubank", "Conta Corrente", "Moradia", "Moradia", "Despesa", "FIXO", null, null, Today);

    private static CheckinSnapshot Snapshot(params OverdueInvoice[] overdue) => new([Rent], [], overdue, []);

    [Fact]
    [Trait("AC", "013-46")]
    public void CheckinMode_NumbersTheInvoiceAfterTheBills_AndShowsHowToAnswer()
    {
        var message = CheckinMessageFormatter.Format(
            Snapshot(new OverdueInvoice(9, "Cartão Inter", new DateOnly(2026, 10, 20), "Fechada", 405.73m, 3)), Today, "abcd1234");

        message.ShouldNotBeNull();
        message.ShouldContain("1. Aluguel - R$ 1.500,00");
        message.ShouldContain("🔴 *FATURAS VENCIDAS:*\n2. Cartão Inter - R$ 405,73 - Venceu em 20/10/2026\n");
        message.ShouldContain("🔹 *COMO RESPONDER:*");
    }

    [Fact]
    [Trait("AC", "013-46")]
    public void CheckinMode_OnlyAnInvoice_StillShowsHowToAnswer()
    {
        var snapshot = new CheckinSnapshot([], [], [new OverdueInvoice(9, "Cartão Inter", new DateOnly(2026, 10, 20), "Fechada", 405.73m, 3)], []);

        var message = CheckinMessageFormatter.Format(snapshot, Today, "abcd1234");

        message.ShouldNotBeNull();
        message.ShouldContain("1. Cartão Inter - R$ 405,73");
        message.ShouldContain("🔹 *COMO RESPONDER:*");
    }

    [Fact]
    [Trait("AC", "013-46")]
    public void DueToday_IsNumberedAfterTheOverdueInvoices()
    {
        var snapshot = new CheckinSnapshot(
            [], [], [new OverdueInvoice(9, "Cartão Inter", new DateOnly(2026, 10, 20), "Fechada", 405.73m, 3)],
            [new DueTodayInvoice(10, "Nubank", Today, "Fechada", 99.9m, 4)]);

        var message = CheckinMessageFormatter.Format(snapshot, Today, "abcd1234");

        message.ShouldNotBeNull();
        message.ShouldContain("1. Cartão Inter - R$ 405,73");
        message.ShouldContain("2. 💳 Nubank - R$ 99,90 - Vence HOJE");
    }

    [Fact]
    [Trait("AC", "013-46")]
    public void ReadOnlyMode_KeepsTheBulletAndNoInstructions()
    {
        var message = CheckinMessageFormatter.Format(
            new CheckinSnapshot([], [], [new OverdueInvoice(9, "Cartão Inter", new DateOnly(2026, 10, 20), "Fechada", 405.73m, 3)], []), Today, null);

        message.ShouldNotBeNull();
        message.ShouldContain("• Cartão Inter - R$ 405,73");
        message.ShouldNotContain("COMO RESPONDER");
    }

    [Fact]
    public void Session_NumbersTheInvoicesAfterTheBills_AndCountsThemInTheTotal()
    {
        var invoices = CheckinInvoiceItem.FromSnapshot(
            new CheckinSnapshot([], [], [new OverdueInvoice(9, "Cartão Inter", new DateOnly(2026, 10, 20), "Fechada", 405.73m, 3)],
                [new DueTodayInvoice(10, "Nubank", Today, "Fechada", 99.9m, 4)]), userId: 7);

        var session = CheckinSession.Build([Rent], Today, invoices);

        session.TotalItems.ShouldBe(3);
        session.Items.Keys.ShouldBe(["1"]);
        session.Invoices.Keys.ShouldBe(["2", "3"]);
        session.Invoices["2"].CardAccountId.ShouldBe(3);
        session.Invoices["3"].Card.ShouldBe("Nubank");
    }

    [Theory]
    [InlineData("1", 3, CheckinInvoiceReplyKind.Account, 1)]
    [InlineData("3", 3, CheckinInvoiceReplyKind.Account, 3)]
    [InlineData("ok", 1, CheckinInvoiceReplyKind.Account, 1)]
    [InlineData("👍", 1, CheckinInvoiceReplyKind.Account, 1)]
    [InlineData("ok", 2, CheckinInvoiceReplyKind.Invalid, 0)]
    [InlineData("4", 3, CheckinInvoiceReplyKind.Invalid, 0)]
    [InlineData("0", 3, CheckinInvoiceReplyKind.Invalid, 0)]
    [InlineData("banana", 3, CheckinInvoiceReplyKind.Invalid, 0)]
    [InlineData("1", 0, CheckinInvoiceReplyKind.Invalid, 0)]
    [InlineData("pular", 3, CheckinInvoiceReplyKind.Skip, 0)]
    [InlineData("Depois.", 3, CheckinInvoiceReplyKind.Skip, 0)]
    [InlineData("não", 0, CheckinInvoiceReplyKind.Skip, 0)]
    public void ReplyParser_ReadsTheAccountNumberOrSkip(string text, int accounts, CheckinInvoiceReplyKind kind, int index)
    {
        CheckinInvoiceReplyParser.Parse(text, accounts).ShouldBe(new CheckinInvoiceReply(kind, index));
    }
}
