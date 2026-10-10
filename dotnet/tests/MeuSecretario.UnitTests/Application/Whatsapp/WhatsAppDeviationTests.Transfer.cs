using System.Text.Json;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

// Desvio 151: transferência por WhatsApp.
public partial class WhatsAppDeviationTests
{
    private static FinanceRig TransferRig(double balance, int originId = 1, int destinationId = 3)
    {
        var config = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            intent = "Transferência",
            accounts = new object[]
            {
                new { id = 1, nome_conta = "Nubank", tipo_conta = "Conta Corrente" },
                new { id = 3, nome_conta = "Carteira", tipo_conta = "Dinheiro" },
            },
            llm = new Dictionary<string, string>
            {
                ["extract_transfer_details"] = JsonSerializer.Serialize(new { valor_decimal = 100, conta_origem = "Nubank", conta_destino = "Carteira" }),
            },
            returns = new Dictionary<string, object>
            {
                ["finance_service.get_account_by_name"] = new { __seq__ = new[] { originId, destinationId } },
                ["finance_service.get_saldo_contas"] = new[] { new { nome_conta = "Nubank", tipo_conta = "Conta Corrente", saldo = balance } },
            },
        })).RootElement.Clone();
        return new FinanceRig(config);
    }

    [Fact]
    [Trait("AC", "012-120")]
    public async Task Desvio151_TransferAsksForConfirmationThenPostsThePair()
    {
        var rig = TransferRig(balance: 500);

        var confirmation = await rig.SendAsync("transferir 100 do nubank para a carteira", Number);

        confirmation.ShouldBe(
            "🔄 *Transferência a confirmar:*\n\n📝 Descrição: Transferência\n💵 Valor: R$ 100,00\n\n📤 De: Nubank\n📥 Para: Carteira\n\n" +
            "Responda:\n• *confirmar* - para realizar transferência\n• *cancelar* - para descartar");
        (await rig.Cache.KeysByPatternAsync("pending_tx:*")).Count.ShouldBe(1);
        rig.World.PostedTransfers.ShouldBeEmpty();

        var done = await rig.SendAsync("OK", Number);

        done.ShouldBe("✅ *Transferência realizada!*\n\n💵 Valor: R$ 100,00\n📤 De: Nubank\n📥 Para: Carteira");
        var posted = rig.World.PostedTransfers.ShouldHaveSingleItem();
        (posted.FromAccountId, posted.ToAccountId, posted.Amount).ShouldBe((1, 3, 100m));
        (await rig.Cache.KeysByPatternAsync("*")).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-120")]
    public async Task Desvio151_InsufficientBalanceBlocksBeforeAnyPending()
    {
        var rig = TransferRig(balance: 40);

        var reply = await rig.SendAsync("transferir 100 do nubank para a carteira", Number);

        reply.ShouldBe("❌ Saldo insuficiente na conta de origem.\n\n💰 Saldo disponível: R$ 40,00\n💸 Valor solicitado: R$ 100,00");
        (await rig.Cache.KeysByPatternAsync("*")).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-120")]
    public async Task Desvio151_SameResolvedAccountIsRefused()
    {
        var rig = TransferRig(balance: 500, originId: 1, destinationId: 1);

        (await rig.SendAsync("transferir 100 do nubank para o banco", Number)).ShouldBe("❌ As contas de origem e destino devem ser diferentes.");
        (await rig.Cache.KeysByPatternAsync("*")).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-120")]
    public async Task Desvio151_TrocarDoesNotApplyAndCancelDiscards()
    {
        var rig = TransferRig(balance: 500);
        await rig.SendAsync("transferir 100 do nubank para a carteira", Number);

        (await rig.SendAsync("TROCAR", Number)).ShouldBe("Transferência não tem categoria. Digite: CONFIRMAR ou CANCELAR.");
        (await rig.Cache.KeysByPatternAsync("pending_tx:*")).Count.ShouldBe(1);

        (await rig.SendAsync("cancelar", Number)).ShouldBe("❌ Transação cancelada.");
        (await rig.Cache.KeysByPatternAsync("*")).ShouldBeEmpty();
        rig.World.PostedTransfers.ShouldBeEmpty();
    }
}
