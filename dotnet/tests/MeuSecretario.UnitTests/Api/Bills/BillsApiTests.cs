using MeuSecretario.Api.Bills;
using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Bills;
using MeuSecretario.UnitTests.TestSupport;

namespace MeuSecretario.UnitTests.Api.Bills;

public class BillsMessagesTests
{
    [Theory]
    [Trait("AC", "005-12")]
    [InlineData(BillError.DataMissing, "Dados não fornecidos")]
    [InlineData(BillError.DescriptionRequired, "Descrição é obrigatória")]
    [InlineData(BillError.DescriptionTooLong, "Descrição deve ter no máximo 255 caracteres")]
    [InlineData(BillError.TypeInvalidCreate, "Tipo de agendamento inválido. Valores permitidos: FIXO, LEMBRETE_VARIAVEL, PARCELADO")]
    [InlineData(BillError.PeriodicityInvalidCreate, "Periodicidade inválida. Valores permitidos: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL")]
    [InlineData(BillError.SubcategoryRequired, "Subcategoria é obrigatória")]
    [InlineData(BillError.AccountRequired, "Conta bancária é obrigatória")]
    [InlineData(BillError.DayInvalid, "Dia de execução deve ser entre 1 e 31")]
    [InlineData(BillError.AmountRequired, "Valor previsto é obrigatório para contas fixas ou parceladas")]
    [InlineData(BillError.AmountNotPositive, "Valor previsto deve ser um número positivo")]
    [InlineData(BillError.InstallmentsInvalid, "Número de parcelas deve ser um inteiro maior ou igual a 2")]
    [InlineData(BillError.MonthInvalidCreate, "Mês de execução deve ser entre 1 e 12")]
    [InlineData(BillError.MonthRequiredForAnnual, "Mês de execução é obrigatório para periodicidade anual")]
    [InlineData(BillError.StartDateInvalid, "Data de início deve estar no formato YYYY-MM-DD")]
    [InlineData(BillError.ReserveFlagInvalid, "Campo 'incluir_na_reserva' deve ser verdadeiro ou falso")]
    [InlineData(BillError.DescriptionEmpty, "Descrição não pode ser vazia")]
    [InlineData(BillError.TypeInvalidUpdate, "Tipo inválido. Valores: FIXO, LEMBRETE_VARIAVEL, PARCELADO")]
    [InlineData(BillError.PeriodicityInvalidUpdate, "Periodicidade inválida. Valores: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL")]
    [InlineData(BillError.MonthInvalidUpdate, "Mês deve ser entre 1 e 12")]
    [InlineData(BillError.AmountInvalidUpdate, "Valor previsto inválido")]
    [InlineData(BillError.InstallmentsRequired, "Número de parcelas é obrigatório para contas parceladas")]
    [InlineData(BillError.InstallmentsBelowExecuted, "Número de parcelas não pode ser menor que as parcelas já executadas")]
    [InlineData(BillError.NothingToUpdate, "Nenhum campo para atualizar")]
    public void EveryValidationError_HasItsExactText(BillError error, string expected)
    {
        BillsMessages.For(error).ShouldBe(expected);
    }

    [Fact]
    public void EveryBillError_HasAMessage()
    {
        foreach (var error in Enum.GetValues<BillError>())
        {
            BillsMessages.For(error).ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    [Trait("AC", "005-84")]
    public void FixedTexts_AreTheExactFlaskOnes()
    {
        BillsMessages.NotFound.ShouldBe("Conta mensal não encontrada");
        BillsMessages.AccountNotFound.ShouldBe("Conta bancária não encontrada");
        BillsMessages.SubcategoryNotFound.ShouldBe("Subcategoria não encontrada");
        BillsMessages.Deleted.ShouldBe("Conta mensal removida com sucesso");
        BillsMessages.ListFailed.ShouldBe("Erro ao carregar contas mensais");
        BillsMessages.CreateFailed.ShouldBe("Erro ao criar conta mensal");
        BillsMessages.UpdateFailed.ShouldBe("Erro ao atualizar conta mensal");
        BillsMessages.DeleteFailed.ShouldBe("Erro ao remover conta mensal");
        BillsMessages.SummaryFailed.ShouldBe("Erro ao calcular resumo");
    }
}

public class BillsResponsesTests
{
    private static readonly BillDetails Details = new(
        41, "Internet", 120m, "FIXO", "MENSAL", 10, null, 3, 12, "Internet", 3, "Mercado pago",
        new DateOnly(2026, 10, 4), false, null, 0);

    [Fact]
    [Trait("AC", "005-01")]
    public async Task List_HasNineteenKeysInOrder_WithTipoTransacaoDerived()
    {
        IReadOnlyList<BillListItem> items =
        [
            new(7, "Aluguel", 1500m, "FIXO", "MENSAL", 5, null, 3, 12, "Aluguel", "Moradia e Contas", "Despesa Essencial",
                3, "Mercado pago", new DateOnly(2026, 1, 5), true, null, 0),
        ];

        var (status, json) = await ResultRunner.Run(BillsResponses.List(items));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"success","data":[{"id":7,"descricao":"Aluguel","valor_previsto":1500,"tipo_agendamento":"FIXO","tipo_transacao":"Despesa","grupo":"Despesa Essencial","periodicidade":"MENSAL","dia_execucao":5,"mes_execucao":null,"notificar_antes_dias":3,"subcategoria_id":12,"subcategoria_nome":"Aluguel","macro_categoria_nome":"Moradia e Contas","conta_id":3,"conta_nome":"Mercado pago","data_inicio":"2026-01-05","incluir_na_reserva":true,"total_parcelas":null,"parcelas_executadas":0}]}""");
    }

    [Theory]
    [Trait("AC", "005-07")]
    [InlineData("Renda", "Renda Principal", "Receita")]
    [InlineData("Geral", "Receitas Gerais", "Receita")]
    [InlineData("Geral", "Despesas Gerais", "Despesa")]
    [InlineData(null, null, "Despesa")]
    public async Task List_TipoTransacao_IsReceitaForRendaOrReceitasGerais(string? group, string? macro, string expected)
    {
        IReadOnlyList<BillListItem> items =
        [
            new(1, "x", null, "LEMBRETE_VARIAVEL", "MENSAL", 1, null, null, 1, null, macro, group, 1, null, new DateOnly(2026, 1, 1), null, null, null),
        ];

        var (_, json) = await ResultRunner.Run(BillsResponses.List(items));

        json.ShouldContain($"\"tipo_transacao\":\"{expected}\"");
        json.ShouldContain("\"valor_previsto\":null");
        json.ShouldContain("\"incluir_na_reserva\":null");
    }

    [Fact]
    [Trait("AC", "005-05")]
    public async Task List_Empty_StillSendsData()
    {
        var (status, json) = await ResultRunner.Run(BillsResponses.List([]));

        status.ShouldBe(200);
        json.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    [Trait("AC", "005-06")]
    public async Task List_AmountWithCents_IsANumber()
    {
        IReadOnlyList<BillListItem> items =
        [
            new(1, "x", 1500.5m, "FIXO", "MENSAL", 1, null, 3, 1, "s", "m", "g", 1, "c", new DateOnly(2026, 1, 1), true, null, 0),
        ];

        var (_, json) = await ResultRunner.Run(BillsResponses.List(items));

        json.ShouldContain("\"valor_previsto\":1500.5,");
    }

    [Fact]
    [Trait("AC", "005-10")]
    public async Task Created_Is201WithSixteenKeys_AndNoMessage()
    {
        var (status, json) = await ResultRunner.Run(BillsResponses.Created(Details));

        status.ShouldBe(201);
        json.ShouldBe(
            """{"status":"success","data":{"id":41,"descricao":"Internet","valor_previsto":120,"tipo_agendamento":"FIXO","periodicidade":"MENSAL","dia_execucao":10,"mes_execucao":null,"notificar_antes_dias":3,"subcategoria_id":12,"subcategoria_nome":"Internet","conta_id":3,"conta_nome":"Mercado pago","data_inicio":"2026-10-04","incluir_na_reserva":false,"total_parcelas":null,"parcelas_executadas":0}}""");
    }

    [Fact]
    [Trait("AC", "005-42")]
    public async Task Updated_Is200WithTheSameSixteenKeys_AndNoMessage()
    {
        var (status, json) = await ResultRunner.Run(BillsResponses.Updated(Details with { Description = "Nova" }));

        status.ShouldBe(200);
        json.ShouldStartWith("""{"status":"success","data":{"id":41,"descricao":"Nova",""");
        json.ShouldNotContain("message");
        json.ShouldNotContain("tipo_transacao");
        json.ShouldNotContain("macro_categoria_nome");
    }

    [Fact]
    [Trait("AC", "005-70")]
    public async Task Deleted_HasMessageAndNoData()
    {
        var (status, json) = await ResultRunner.Run(BillsResponses.Deleted());

        status.ShouldBe(200);
        json.ShouldBe("""{"status":"success","message":"Conta mensal removida com sucesso"}""");
    }

    [Fact]
    [Trait("AC", "005-98")]
    public async Task Summary_KeepsTheOrder_AndMonthsIsAnInteger()
    {
        var summary = new BillsSummary(2143.3, 1200, 3000, 14400, 2400, 6);

        var (status, json) = await ResultRunner.Run(BillsResponses.Summary(summary));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"success","data":{"total_despesas_mensais":2143.3,"total_despesas_anuais":1200,"total_receitas_mensais":3000,"reserva_emergencia":{"meta":14400,"gasto_mensal_equivalente":2400,"meses_configurados":6}}}""");
    }

    [Fact]
    [Trait("AC", "005-84")]
    public async Task Error_IsTheEnvelopeAWithTheFixedText()
    {
        var (status, json) = await ResultRunner.Run(BillsResponses.Error(BillsMessages.ListFailed, 500));

        status.ShouldBe(500);
        json.ShouldBe("""{"status":"error","message":"Erro ao carregar contas mensais"}""");
    }
}
