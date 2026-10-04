using MeuSecretario.Api.Transactions;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.UnitTests.TestSupport;

namespace MeuSecretario.UnitTests.Api.Transactions;

public class TransactionsResponsesTests
{
    private static NewTransactionData Input(decimal amount, TransactionKind kind, string dateText = "2025-12-11") =>
        new("Supermercado", amount, kind, dateText, new DateOnly(2025, 12, 11), 7, 3, null, true);

    [Fact]
    [Trait("AC", "004-10")]
    public async Task Created_EchoesTheSignedValueAsSentAndTheDateText()
    {
        var (status, json) = await ResultRunner.Run(
            TransactionsResponses.Created(Input(150.5m, TransactionKind.Expense, "2025-1-5"), new PostedTransaction(1234, -150.5m, null, null)));

        status.ShouldBe(201);
        json.ShouldBe(
            """{"status":"success","message":"Transação criada com sucesso","data":{"id":1234,"descricao":"Supermercado","valor":-150.5,"tipo":"Despesa","data":"2025-1-5"}}""");
    }

    [Fact]
    [Trait("AC", "004-11")]
    public async Task Created_Income_EchoesReceita()
    {
        var (_, json) = await ResultRunner.Run(
            TransactionsResponses.Created(Input(3000m, TransactionKind.Income), new PostedTransaction(1, 3000m, null, null)));

        json.ShouldContain("\"valor\":3000,\"tipo\":\"Receita\"");
    }

    [Fact]
    [Trait("AC", "004-16")]
    public async Task Created_DoesNotRoundTheValue()
    {
        var (_, json) = await ResultRunner.Run(
            TransactionsResponses.Created(Input(10.005m, TransactionKind.Expense), new PostedTransaction(1, -10.01m, null, null)));

        json.ShouldContain("\"valor\":-10.005,");
    }

    [Fact]
    [Trait("AC", "004-15")]
    public async Task Created_ZeroExpense_IsNegativeZero()
    {
        var (_, json) = await ResultRunner.Run(
            TransactionsResponses.Created(Input(0m, TransactionKind.Expense), new PostedTransaction(1, 0m, null, null)));

        json.ShouldContain("\"valor\":-0,");
    }

    [Fact]
    [Trait("AC", "004-42")]
    public async Task Updated_MapsRendaToReceita_AndWritesAnIsoDate()
    {
        var (status, json) = await ResultRunner.Run(
            TransactionsResponses.Updated(new UpdatedTransaction(9, "Salário", 3000m, "Renda", new DateOnly(2026, 3, 5))));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"success","message":"Transação atualizada com sucesso","data":{"id":9,"descricao":"Salário","valor":3000,"tipo":"Receita","data":"2026-03-05"}}""");
    }

    [Fact]
    [Trait("AC", "004-70")]
    public async Task Deleted_HasNoData()
    {
        var (status, json) = await ResultRunner.Run(TransactionsResponses.Deleted());

        status.ShouldBe(200);
        json.ShouldBe("""{"status":"success","message":"Transação deletada com sucesso"}""");
    }

    [Fact]
    [Trait("AC", "004-01")]
    public async Task List_KeepsTheKeyOrder_WithTheExtrasAtTheEnd()
    {
        var item = new TransactionListItem(
            1, "Supermercado", -70.55m, "Despesa", new DateOnly(2025, 12, 11), "Despesas Gerais", "Outros", "Nubank", true, 3, 59, "nota");

        var (status, json) = await ResultRunner.Run(
            TransactionsResponses.List(new TransactionPage(245, [item]), new ListFilter(20, 0, null, null, null)));

        status.ShouldBe(200);
        json.ShouldBe(
            """{"status":"success","data":{"total":245,"limit":20,"offset":0,"transactions":[{"id":1,"descricao":"Supermercado","valor":-70.55,"tipo":"Despesa","data_transacao":"2025-12-11","categoria":"Despesas Gerais","subcategoria":"Outros","conta":"Nubank","consolidada":true,"conta_id":3,"subcategoria_id":59,"observacoes":"nota","data":"2025-12-11","conta_bancaria":"Nubank","conta_bancaria_id":3}]}}""");
    }

    [Fact]
    [Trait("AC", "004-04")]
    public async Task List_ShowsRendaAsReceita_AndKeepsTransferTypes()
    {
        TransactionListItem Item(string type) =>
            new(1, "x", 1m, type, new DateOnly(2025, 1, 1), "c", "s", "a", true, 1, 1, null);

        var (_, json) = await ResultRunner.Run(TransactionsResponses.List(
            new TransactionPage(3, [Item("Renda"), Item("Transferência"), Item("Pagamento Fatura")]), new ListFilter(20, 0, null, null, null)));

        json.ShouldContain("\"tipo\":\"Receita\"");
        json.ShouldContain("\"tipo\":\"Transferência\"");
        json.ShouldContain("\"tipo\":\"Pagamento Fatura\"");
        json.ShouldNotContain("\"tipo\":\"Renda\"");
    }

    [Fact]
    [Trait("AC", "004-83")]
    public async Task Error_IsEnvelopeA()
    {
        var (status, json) = await ResultRunner.Run(TransactionsResponses.Error(TransactionsMessages.CreateFailed, 500));

        status.ShouldBe(500);
        json.ShouldBe("""{"status":"error","message":"Erro ao criar transação"}""");
    }
}

public class TransactionsMessagesTests
{
    [Fact]
    public void EveryErrorHasAMessage_AndTheLiteralTextsAreExact()
    {
        foreach (var error in Enum.GetValues<TransactionError>())
        {
            TransactionsMessages.For(error).ShouldNotBeNullOrWhiteSpace();
        }
        foreach (var error in Enum.GetValues<ListQueryError>())
        {
            TransactionsMessages.For(error).ShouldNotBeNullOrWhiteSpace();
        }

        TransactionsMessages.For(TransactionError.DescriptionRequired).ShouldBe("Campo 'descricao' é obrigatório");
        TransactionsMessages.For(TransactionError.ValueNegative).ShouldBe("Valor deve ser positivo");
        TransactionsMessages.For(TransactionError.TypeInvalid).ShouldBe("Tipo deve ser 'Receita' ou 'Despesa'");
        TransactionsMessages.For(TransactionError.DateFormat).ShouldBe("Data deve estar no formato YYYY-MM-DD");
        TransactionsMessages.For(TransactionError.NoDataProvided).ShouldBe("Nenhum dado fornecido para atualização");
        TransactionsMessages.For(TransactionError.NoValidField).ShouldBe("Nenhum campo válido para atualização");
        TransactionsMessages.For(TransactionError.NotEditable)
            .ShouldBe("Transferências e pagamentos de fatura não podem ser editados. Exclua e lance novamente.");
        TransactionsMessages.For(ListQueryError.LimitRange).ShouldBe("Parâmetro 'limit' deve estar entre 1 e 100");
        TransactionsMessages.For(ListQueryError.OffsetNegative).ShouldBe("Parâmetro 'offset' deve ser maior ou igual a 0");
        TransactionsMessages.For(ListQueryError.DateFormat).ShouldBe("Datas devem estar no formato YYYY-MM-DD");
    }
}
