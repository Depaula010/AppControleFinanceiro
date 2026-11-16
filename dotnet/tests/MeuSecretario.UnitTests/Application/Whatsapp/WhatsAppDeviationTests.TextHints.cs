using System.Text.Json;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

// Desvio 281 (antigo 154): data, parcelas e valor da parcela lidos do texto de "gastei/comprei".
public partial class WhatsAppDeviationTests
{
    private static async Task<JsonElement> PendingOfAsync(FinanceRig rig)
    {
        var key = (await rig.Cache.KeysByPatternAsync("pending_tx:*")).ShouldHaveSingleItem();
        return (await rig.Cache.GetJsonElementAsync(key))!.Value;
    }

    [Fact]
    [Trait("AC", "012-125")]
    public async Task Desvio281_YesterdayInTheTextBecomesTheTransactionDate_AndShowsInTheConfirmation()
    {
        var rig = await RigAsync("despesa_simples");

        var reply = await rig.SendAsync("gastei 80 ontem", Number);

        reply.ShouldContain("📅 06/10/2026");
        (await PendingOfAsync(rig)).GetProperty("data_transacao").GetString().ShouldBe("2026-10-06");
    }

    [Fact]
    [Trait("AC", "012-125")]
    public async Task Desvio281_NoDateInTheText_KeepsTodayAndHidesTheDateLine()
    {
        var rig = await RigAsync("despesa_simples");

        var reply = await rig.SendAsync("gastei 50 na padaria", Number);

        reply.ShouldNotContain("📅");
        (await PendingOfAsync(rig)).GetProperty("data_transacao").GetString().ShouldBe("2026-10-07");
    }

    [Fact]
    [Trait("AC", "012-125")]
    public async Task Desvio281_DateOnACardPurchase_NoLongerFails()
    {
        var rig = await RigAsync("despesa_conta_mencionada_cartao");

        var reply = await rig.SendAsync("gastei 80 ontem no cartão inter", Number);

        reply.ShouldStartWith("💰 *CONFIRME");
        (await PendingOfAsync(rig)).GetProperty("data_transacao").GetString().ShouldBe("2026-10-06");
    }

    [Fact]
    [Trait("AC", "012-125")]
    public async Task Desvio281_InstallmentsInTheText_AreUsedWithTheTotalFromTheModel()
    {
        var rig = await RigAsync("despesa_simples");

        await rig.SendAsync("comprei tv 50 em 10x", Number);

        var pending = await PendingOfAsync(rig);
        pending.GetProperty("num_parcelas").GetInt32().ShouldBe(10);
        pending.GetProperty("valor_total").GetDouble().ShouldBe(50);
        pending.GetProperty("valor_original").GetDouble().ShouldBe(5);
    }

    [Fact]
    [Trait("AC", "012-125")]
    public async Task Desvio281_TenTimesOfAValue_MeansTenInstallmentsOfThatValue()
    {
        var rig = await RigAsync("despesa_simples");

        await rig.SendAsync("comprei tv em 10x de 300", Number);

        var pending = await PendingOfAsync(rig);
        pending.GetProperty("num_parcelas").GetInt32().ShouldBe(10);
        pending.GetProperty("valor_total").GetDouble().ShouldBe(3000);
        pending.GetProperty("valor_original").GetDouble().ShouldBe(300);
    }

    [Fact]
    [Trait("AC", "012-125")]
    public async Task Desvio281_MoreThan48Installments_IsRefused()
    {
        var rig = await RigAsync("despesa_simples");

        (await rig.SendAsync("comprei tv 50 em 60x", Number)).ShouldBe("❌ Número de parcelas inválido (deve ser entre 2 e 48).");
    }
}
