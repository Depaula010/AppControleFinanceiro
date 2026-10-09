using MeuSecretario.Api.Automation;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Automation;
using MeuSecretario.UnitTests.TestSupport;

namespace MeuSecretario.UnitTests.Api.Automation;

public class AutomationResponsesTests
{
    [Fact]
    [Trait("AC", "011-016")]
    public async Task AutomateSaved_HasOnlyTheStatus()
    {
        var (status, json) = await ResultRunner.Run(AutomationResponses.AutomateSaved());

        status.ShouldBe(200);
        json.ShouldBe("""{"status":"sucesso"}""");
    }

    [Fact]
    [Trait("AC", "011-011")]
    public async Task AutomatePending_PutsTheIdAtTheRoot()
    {
        var (_, json) = await ResultRunner.Run(AutomationResponses.AutomatePending("ab12cd34"));

        json.ShouldBe("""{"status":"sucesso","mensagem":"Aguardando confirmacao do usuario","transaction_id":"ab12cd34"}""");
    }

    [Fact]
    [Trait("AC", "011-038")]
    public async Task ApiSuccess_UsesTheEnglishEnvelopeWithTheExtrasInOrder()
    {
        var (status, json) = await ResultRunner.Run(AutomationResponses.ApiSuccess(
            AutomationMessages.ApiPending,
            new Dictionary<string, object?>
            {
                ["transaction_id"] = "ab12cd34", ["categoria_sugerida"] = "A -> B", ["conta_utilizada"] = "Nubank", ["vinculado_fatura"] = true,
            }));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"success","message":"Transacao pendente de confirmacao no WhatsApp","transaction_id":"ab12cd34","categoria_sugerida":"A -> B","conta_utilizada":"Nubank","vinculado_fatura":true}""");
    }

    [Fact]
    [Trait("AC", "011-030")]
    public async Task ApiInvalidJson_HasOnlyTheErroKey()
    {
        var (status, json) = await ResultRunner.Run(AutomationResponses.ApiInvalidJson());

        status.ShouldBe(400);
        json.ShouldBe("""{"erro":"JSON invalido ou ausente"}""");
    }

    [Fact]
    [Trait("AC", "011-060")]
    public async Task SmsDatabaseUnavailable_Is503WithTheRespostaKey()
    {
        var (status, json) = await ResultRunner.Run(AutomationResponses.SmsDatabaseUnavailable());

        status.ShouldBe(503);
        json.ShouldBe("""{"status":"erro","resposta":"Banco de dados temporariamente indisponivel"}""");
    }

    [Fact]
    [Trait("AC", "011-063")]
    public async Task SmsRegistered_CarriesANullIdentifiedBill()
    {
        var (_, json) = await ResultRunner.Run(AutomationResponses.SmsRegistered());

        json.ShouldBe("""{"status":"sucesso","mensagem":"Despesa registrada","conta_identificada":null}""");
    }

    [Fact]
    [Trait("AC", "011-081")]
    public async Task ToggleUpdated_KeepsTheFlaskKeyOrderAndNumbers()
    {
        var updated = new ToggleReserveResult.Updated(
            new ReserveScheduleRow("Netflix", 49.90m, "MENSAL", "FIXO"), true, new EmergencyReserveStatus(2500, 15000, 6), new EmergencyReserveStatus(2549.9, 15299.4, 6));

        var (_, json) = await ResultRunner.Run(AutomationResponses.ToggleUpdated(updated, 123));

        json.ShouldBe(
            """{"status":"sucesso","mensagem":"Agendamento 'Netflix' incluído do cálculo de reserva","agendamento":{"id":123,"descricao":"Netflix","valor_previsto":49.9,"periodicidade":"MENSAL","tipo_agendamento":"FIXO","incluir_na_reserva":true},"impacto":{"gasto_mensal_anterior":2500,"gasto_mensal_novo":2549.9,"reserva_ideal_anterior":15000,"reserva_ideal_nova":15299.4}}""");
    }

    [Fact]
    [Trait("AC", "011-101")]
    public async Task Listed_KeepsKeyOrder_ZeroForMissingAmountAndNullFlag()
    {
        var page = new ReserveListPage(
            3, [new ReserveListItem(7, "Sem valor", null, "MENSAL", "FIXO", 5, "Outros", "Despesas Gerais", "Geral", null)]);
        var result = new ListReserveResult.Listed(new ReserveListFilter(null, null, null, 100, 0), page, new EmergencyReserveStatus(0, 0, 6));

        var (_, json) = await ResultRunner.Run(AutomationResponses.Listed(result));

        json.ShouldBe(
            """{"status":"sucesso","total":3,"limit":100,"offset":0,"agendamentos":[{"id":7,"descricao":"Sem valor","valor_previsto":0,"periodicidade":"MENSAL","tipo_agendamento":"FIXO","dia_execucao":5,"categoria":"Outros","macro_categoria":"Despesas Gerais","grupo":"Geral","incluir_na_reserva":null}],"resumo_reserva":{"gasto_mensal_essencial":0,"reserva_ideal":0,"meses_configurados":6}}""");
    }

    [Fact]
    [Trait("AC", "011-122")]
    public async Task Reprocessed_ShowsDecimalsAsTextAndZeroAsNumber()
    {
        var stats = new InvoiceScheduleStats(
            2, 1, 0, 1, [new InvoiceScheduleDetail("FIXO", "Netflix", "49.90"), new InvoiceScheduleDetail("LEMBRETE_VARIAVEL", "Variável", 0)]);
        var result = new ReprocessInvoiceResult.Processed(new ReprocessTarget(5, 3, "Cartão Inter", new DateOnly(2026, 4, 5), "Aberta"), stats);

        var (status, json) = await ResultRunner.Run(AutomationResponses.Reprocessed(result));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"sucesso","fatura_id":5,"cartao":"Cartão Inter","vencimento":"05/04/2026","status_fatura":"Aberta","stats":{"total_processados":2,"fixos":1,"parcelados":0,"lembretes":1,"erros":0,"detalhes":[{"tipo":"FIXO","descricao":"Netflix","valor":"49.90"},{"tipo":"LEMBRETE_VARIAVEL","descricao":"Variável","valor":0}]}}""");
    }

    [Fact]
    [Trait("AC", "011-126")]
    public async Task Reprocessed_ReportsTheSchedulesThatFailed()
    {
        // Desvio 101: o 200 só conta o que foi gravado e stats ganha a contagem de erros.
        var stats = new InvoiceScheduleStats(1, 1, 0, 0, [new InvoiceScheduleDetail("FIXO", "Netflix", "49.90")], 2);
        var result = new ReprocessInvoiceResult.Processed(new ReprocessTarget(5, 3, "Cartão Inter", new DateOnly(2026, 4, 5), "Aberta"), stats);

        var (status, json) = await ResultRunner.Run(AutomationResponses.Reprocessed(result));

        status.ShouldBe(200);
        json.ShouldContain("\"total_processados\":1,\"fixos\":1,\"parcelados\":0,\"lembretes\":0,\"erros\":2,");
    }

    [Fact]
    [Trait("AC", "011-120")]
    public async Task Unauthorized_IsEnvelopeBWithAccent()
    {
        var (status, json) = await ResultRunner.Run(AutomationResponses.Unauthorized(new Microsoft.AspNetCore.Http.DefaultHttpContext()));

        status.ShouldBe(401);
        json.ShouldBe("""{"status":"erro","mensagem":"Não autorizado"}""");
    }
}

public class AutomationMessagesTests
{
    [Fact]
    [Trait("AC", "011-031")]
    public void MissingFields_JoinsInOrder() =>
        AutomationMessages.MissingFields(["valor", "local"]).ShouldBe("Campos obrigatorios faltando: valor, local");

    [Fact]
    [Trait("AC", "011-032")]
    public void InvalidValue_PrefixesThePythonReason() =>
        AutomationMessages.InvalidValue("Valor deve ser positivo").ShouldBe("Valor invalido: Valor deve ser positivo");

    [Fact]
    [Trait("AC", "011-033")]
    public void InvalidPaymentType_ListsTheFourTypes() =>
        AutomationMessages.InvalidPaymentType().ShouldBe("tipo_pagamento invalido. Use: credito, debito, pix, dinheiro");

    [Fact]
    [Trait("AC", "011-035")]
    public void AccountNotFound_QuotesTheName() =>
        AutomationMessages.AccountNotFound("Nubank").ShouldBe("Conta 'Nubank' nao encontrada. Verifique o nome exato.");

    [Theory]
    [Trait("AC", "011-081")]
    [InlineData(true, "Agendamento 'Luz' incluído do cálculo de reserva")]
    [InlineData(false, "Agendamento 'Luz' excluído do cálculo de reserva")]
    public void ToggleSaved_UsesTheFlaskWording(bool include, string expected) => AutomationMessages.ToggleSaved("Luz", include).ShouldBe(expected);

    [Fact]
    [Trait("AC", "011-121")]
    public void InvoiceNotFound_UsesTheId() => AutomationMessages.InvoiceNotFound(9999).ShouldBe("Fatura ID 9999 não encontrada");

    [Fact]
    [Trait("AC", "011-005")]
    public void TheFixed500Texts_NeverMentionAnException() =>
        new[]
        {
            AutomationMessages.AutomateFailed, AutomationMessages.ApiTransactionFailed, AutomationMessages.SmsFailed,
            AutomationMessages.ToggleFailed, AutomationMessages.ListFailed, AutomationMessages.ReprocessFailed,
        }.ShouldAllBe(text => text.StartsWith("Erro ao ", StringComparison.Ordinal) && text.EndsWith("Tente novamente.", StringComparison.Ordinal));
}
