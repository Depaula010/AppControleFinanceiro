using System.Net;
using MeuSecretario.IntegrationTests.Support;
using static MeuSecretario.IntegrationTests.Transactions.TransactionsTestData;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary><c>PUT /api/transactions/{id}</c> (spec 004 §3.3): uma linha da tabela por teste, corpo como texto exato.</summary>
[Trait("Category", "Docker")]
public class TransactionsPutEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly TransactionsHttp _http = new(postgres);
    private readonly TransactionsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _account;
    private int _id;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync("5511900000001", "Ana");
        _account = await _data.InsertAccountAsync(_user, "Corrente");
        _id = await _data.InsertTransactionAsync(_user, _account, "Despesa", -150.5m, "2025-12-11", "Supermercado");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<(HttpStatusCode Status, string Body)> Put(string? json, int? id = null, int? user = null) =>
        _http.Put(user ?? _user, id ?? _id, json);

    private async Task AssertBadRequest(string? json, string message)
    {
        var (status, body) = await Put(json);

        status.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldBe(TransactionsHttp.Error(message));
    }

    private async Task<(string Desc, decimal Valor, string Tipo, DateOnly Data, int Sub, int Conta, string? Notes, bool Consolidada, int? Fatura)> Row(int? id = null) =>
        (await _data.QueryAsync<(string, decimal, string, DateTime, int, int, string?, bool, int?)>(
            "SELECT descricao, valor, tipo_transacao, data_transacao, subcategoria_id, conta_id, observacoes, consolidada, fatura_id FROM transacoes WHERE id = @id",
            new { id = id ?? _id }))
        .Select(r => (r.Item1, r.Item2, r.Item3, DateOnly.FromDateTime(r.Item4), r.Item5, r.Item6, r.Item7, r.Item8, r.Item9))
        .Single();

    // ---------- corpo e posse ----------

    [Theory]
    [Trait("AC", "004-40")]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("0")]
    public async Task InvalidBody_Returns400_EvenWhenTheTransactionDoesNotExist(string json)
    {
        await AssertBadRequest(json, "Nenhum dado fornecido para atualização");

        var (status, body) = await Put(json, id: 99999);
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(TransactionsHttp.Error("Nenhum dado fornecido para atualização"));
    }

    [Fact]
    [Trait("AC", "004-40")]
    public async Task NoBody_Returns400()
    {
        await AssertBadRequest(null, "Nenhum dado fornecido para atualização");
    }

    [Fact]
    [Trait("AC", "004-41")]
    public async Task UnknownTransaction_Returns404()
    {
        var (status, body) = await Put("""{"descricao":"x"}""", id: 99999);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Transação não encontrada"));
    }

    // ---------- campos ----------

    [Fact]
    [Trait("AC", "004-42")]
    public async Task Description_Returns200WithTheExactBody_OnlyItChanges()
    {
        var (status, body) = await Put("""{"descricao":"Supermercado Extra"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$$"""{"status":"success","message":"Transação atualizada com sucesso","data":{"id":{{{_id}}},"descricao":"Supermercado Extra","valor":-150.5,"tipo":"Despesa","data":"2025-12-11"}}""");
        (await Row()).ShouldBe(("Supermercado Extra", -150.5m, "Despesa", Day("2025-12-11"), ExpenseSubcategory, _account, null, true, null));
    }

    [Fact]
    [Trait("AC", "004-42")]
    public async Task UpdatedAt_Advances()
    {
        await _data.ExecuteAsync("UPDATE transacoes SET updated_at = '2020-01-01' WHERE id = @_id", new { _id });

        await Put("""{"descricao":"Novo"}""");

        (await _data.ScalarAsync<bool>("SELECT updated_at > '2021-01-01' FROM transacoes WHERE id = @_id", new { _id })).ShouldBeTrue();
    }

    [Theory]
    [Trait("AC", "004-52")]
    [InlineData("""{"foo":1}""")]
    [InlineData("""{"descricao":""}""")]
    [InlineData("""{"descricao":null}""")]
    [InlineData("""{"descricao":0}""")]
    public async Task NoValidField_Returns400(string json)
    {
        await AssertBadRequest(json, "Nenhum campo válido para atualização");
    }

    [Fact]
    [Trait("AC", "004-43")]
    public async Task Value_KeepsTheSignOfTheCurrentType()
    {
        var (_, body) = await Put("""{"valor":180}""");
        body.ShouldContain("\"valor\":-180,");
        (await Row()).Valor.ShouldBe(-180m);

        var income = await _data.InsertTransactionAsync(_user, _account, "Despesa", -5m);
        await _data.ExecuteAsync("UPDATE transacoes SET tipo_transacao = 'Renda', valor = 5 WHERE id = @income", new { income });
        var (_, incomeBody) = await Put("""{"valor":180}""", income);
        incomeBody.ShouldContain("\"valor\":180,\"tipo\":\"Receita\"");
        (await Row(income)).Valor.ShouldBe(180m);
    }

    [Fact]
    [Trait("AC", "004-44")]
    public async Task ValueAndType_UseTheBodyType_InAnyKeyOrder()
    {
        var (_, body) = await Put("""{"tipo":"Receita","valor":10}""");

        body.ShouldContain("\"valor\":10,\"tipo\":\"Receita\"");
        (await Row()).ShouldSatisfyAllConditions(r => r.Valor.ShouldBe(10m), r => r.Tipo.ShouldBe("Renda"));
    }

    [Fact]
    [Trait("AC", "004-44")]
    public async Task TypeAlone_RewritesTheMagnitudeWithTheNewSign()
    {
        await Put("""{"tipo":"Receita"}""");
        (await Row()).ShouldSatisfyAllConditions(r => r.Valor.ShouldBe(150.5m), r => r.Tipo.ShouldBe("Renda"));

        await Put("""{"tipo":"Despesa"}""");
        (await Row()).ShouldSatisfyAllConditions(r => r.Valor.ShouldBe(-150.5m), r => r.Tipo.ShouldBe("Despesa"));
    }

    [Fact]
    [Trait("AC", "004-45")]
    public async Task Value_Errors()
    {
        foreach (var invalid in new[] { "\"abc\"", "null", "[]" })
        {
            await AssertBadRequest($$"""{"valor":{{invalid}}}""", "Valor inválido");
        }
        await AssertBadRequest("""{"valor":-5}""", "Valor deve ser positivo");
        (await Put("""{"valor":0}""")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("AC", "004-46")]
    public async Task Type_Errors_AndTheValueErrorComesFirst()
    {
        foreach (var invalid in new[] { "\"X\"", "null", "\"Renda\"" })
        {
            await AssertBadRequest($$"""{"tipo":{{invalid}}}""", "Tipo deve ser 'Receita' ou 'Despesa'");
        }
        await AssertBadRequest("""{"tipo":"X","valor":"abc"}""", "Valor inválido");
    }

    [Fact]
    [Trait("AC", "004-47")]
    public async Task Date_IsUpdated_OrRejected()
    {
        var (status, body) = await Put("""{"data":"2025-12-12"}""");
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"data\":\"2025-12-12\"}");

        await AssertBadRequest("""{"data":"12/12/2025"}""", "Data deve estar no formato YYYY-MM-DD");
        await AssertBadRequest("""{"data":null}""", "Data deve estar no formato YYYY-MM-DD");
    }

    [Fact]
    [Trait("AC", "004-48")]
    public async Task Subcategory_ValidIsUpdated_InvalidIs404()
    {
        (await Put($$"""{"subcategoria_id":{{IncomeSubcategory}}}""")).Status.ShouldBe(HttpStatusCode.OK);
        (await Row()).Sub.ShouldBe(IncomeSubcategory);

        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.ScalarAsync<int>(
            "INSERT INTO subcategoria (macro_id, nome_sub, usuario_id) VALUES (17, 'Dele', @other) RETURNING id", new { other });
        foreach (var invalid in new[] { "99999", "null", foreign.J() })
        {
            var (status, body) = await Put($$"""{"subcategoria_id":{{invalid}}}""");
            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(TransactionsHttp.Error("Subcategoria não encontrada"));
        }
    }

    [Fact]
    [Trait("AC", "004-49")]
    public async Task Account_OwnActiveIsUpdated_OthersAre404_AndTheSubcategoryIsCheckedFirst()
    {
        var second = await _data.InsertAccountAsync(_user, "Poupança");
        (await Put($$"""{"conta_id":{{second}}}""")).Status.ShouldBe(HttpStatusCode.OK);
        (await Row()).Conta.ShouldBe(second);

        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertAccountAsync(other, "Alheia");
        var inactive = await _data.InsertAccountAsync(_user, "Velha", ativa: false);
        foreach (var invalid in new[] { foreign.J(), inactive.J(), "99999", "null" })
        {
            var (status, body) = await Put($$"""{"conta_id":{{invalid}}}""");
            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(TransactionsHttp.Error("Conta não encontrada ou não pertence ao usuário"));
        }

        var both = await Put("""{"subcategoria_id":99999,"conta_id":99999}""");
        both.Body.ShouldBe(TransactionsHttp.Error("Subcategoria não encontrada"));
    }

    [Fact]
    [Trait("AC", "004-50")]
    public async Task Notes_AreTrimmed_AndEmptyOrNullClearThem()
    {
        await Put("""{"observacoes":"  nota  "}""");
        (await Row()).Notes.ShouldBe("nota");

        await Put("""{"observacoes":""}""");
        (await Row()).Notes.ShouldBeNull();

        await Put("""{"observacoes":"de novo"}""");
        await Put("""{"observacoes":null}""");
        (await Row()).Notes.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "004-51")]
    public async Task Consolidated_FalseThenTrue()
    {
        await Put("""{"consolidada":false}""");
        (await Row()).Consolidada.ShouldBeFalse();

        await Put("""{"consolidada":true}""");
        (await Row()).Consolidada.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-53")]
    public async Task TwoErrors_RespondTheFirst_AndNothingIsWritten()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertAccountAsync(other, "Alheia");

        await AssertBadRequest($$"""{"descricao":"Novo","valor":"abc","conta_id":{{foreign}}}""", "Valor inválido");

        (await Row()).Desc.ShouldBe("Supermercado");
    }

    [Fact]
    [Trait("AC", "004-54")]
    public async Task TheBodyTheAngularSends_Returns200_AndReconsolidates()
    {
        await _data.ExecuteAsync("UPDATE transacoes SET consolidada = false WHERE id = @_id", new { _id });
        var json = $$"""{"descricao":"Supermercado","valor":150.5,"tipo":"Despesa","data":"2025-12-11","subcategoria_id":{{ExpenseSubcategory}},"conta_id":{{_account}},"consolidada":true}""";

        var (status, _) = await Put(json);

        status.ShouldBe(HttpStatusCode.OK);
        (await Row()).Consolidada.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-55")]
    public async Task AnotherUsersTransaction_Returns404_AndIsUntouched()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");

        var (status, body) = await Put("""{"descricao":"roubada"}""", user: other);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Transação não encontrada"));
        (await Row()).Desc.ShouldBe("Supermercado");
    }

    [Fact]
    [Trait("AC", "004-55")]
    public async Task AnotherUsersAccountOnMyTransaction_Returns404()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");

        var (status, _) = await Put($$"""{"conta_id":{{otherAccount}}}""");

        status.ShouldBe(HttpStatusCode.NotFound);
        (await Row()).Conta.ShouldBe(_account);
    }

    [Fact]
    [Trait("AC", "004-43")]
    public async Task RendaFromTheBot_KeepsAPositiveSign()
    {
        var bot = await _data.InsertTransactionAsync(_user, _account, "Renda", 3000m, descricao: "salário");

        var (_, body) = await Put("""{"valor":180}""", bot);

        body.ShouldContain("\"valor\":180,\"tipo\":\"Receita\"");
    }

    [Fact]
    [Trait("AC", "004-12")]
    public async Task FormerlyA500_NowA400()
    {
        await AssertBadRequest("""{"descricao":5}""", "Campo 'descricao' inválido");
        await AssertBadRequest($$"""{"descricao":"{{new string('a', 256)}}"}""", "Campo 'descricao' deve ter no máximo 255 caracteres");
        await AssertBadRequest("""{"observacoes":5}""", "Campo 'observacoes' inválido");
        await AssertBadRequest("""{"consolidada":"false"}""", "Campo 'consolidada' inválido");
        await AssertBadRequest("""{"valor":"1e20"}""", "Valor inválido");
    }

    // ---------- desvio 068 ----------

    [Theory]
    [Trait("AC", "004-85")]
    [InlineData("Transferência")]
    [InlineData("Pagamento Fatura")]
    public async Task TransferAndPayment_CannotBeEdited(string type)
    {
        var id = await _data.InsertTransactionAsync(_user, _account, type, -100m);

        var (status, body) = await Put("""{"descricao":"x"}""", id);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(TransactionsHttp.Error("Transferências e pagamentos de fatura não podem ser editados. Exclua e lance novamente."));
        (await Row(id)).Desc.ShouldBe("teste");
    }

    [Fact]
    [Trait("AC", "004-85")]
    public async Task NotEditable_ComesAfterTheNotFound_AndAfterTheEmptyBody()
    {
        var id = await _data.InsertTransactionAsync(_user, _account, "Transferência", -100m);

        (await Put("{}", id)).Body.ShouldBe(TransactionsHttp.Error("Nenhum dado fornecido para atualização"));
        (await Put("""{"descricao":"x"}""", id, user: await _data.InsertUserAsync("5511900000002", "Beto"))).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- desvio 069 ----------

    [Fact]
    [Trait("AC", "004-56")]
    public async Task MovingToACard_LinksTheInvoice_CreatingIt()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);

        (await Put($$"""{"conta_id":{{card}},"data":"2026-03-20"}""")).Status.ShouldBe(HttpStatusCode.OK);

        var invoice = (await _data.QueryAsync<(int Id, DateTime Due)>("SELECT id, data_vencimento FROM faturas")).Single();
        DateOnly.FromDateTime(invoice.Due).ShouldBe(Day("2026-04-15"));
        (await Row()).Fatura.ShouldBe(invoice.Id);
    }

    [Fact]
    [Trait("AC", "004-56")]
    public async Task ChangingTheDateOnACard_MovesToTheNewCycle()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);
        var invoice = await _data.InsertInvoiceAsync(card, "2026-03-15", "2026-03-10");
        var id = await _data.InsertTransactionAsync(_user, card, "Despesa", -50m, "2026-03-10", faturaId: invoice);

        await Put("""{"data":"2026-03-11"}""", id);

        var row = await Row(id);
        row.Fatura.ShouldNotBe(invoice);
        (await _data.ScalarAsync<string>("SELECT data_vencimento::text FROM faturas WHERE id = @f", new { f = row.Fatura })).ShouldBe("2026-04-15");
    }

    [Fact]
    [Trait("AC", "004-56")]
    public async Task MovingToANonCard_OrToIncome_ClearsTheInvoice()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);
        var invoice = await _data.InsertInvoiceAsync(card, "2026-03-15", "2026-03-10");
        var first = await _data.InsertTransactionAsync(_user, card, "Despesa", -50m, "2026-03-10", faturaId: invoice);
        var second = await _data.InsertTransactionAsync(_user, card, "Despesa", -50m, "2026-03-10", faturaId: invoice);

        await Put($$"""{"conta_id":{{_account}}}""", first);
        await Put("""{"tipo":"Receita"}""", second);

        (await Row(first)).Fatura.ShouldBeNull();
        (await Row(second)).Fatura.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "004-56")]
    public async Task EditsThatDoNotAffectTheInvoice_LeaveItAlone()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);
        var invoice = await _data.InsertInvoiceAsync(card, "2026-03-15", "2026-03-10", "Paga");
        var id = await _data.InsertTransactionAsync(_user, card, "Despesa", -50m, "2026-03-10", faturaId: invoice);

        await Put("""{"descricao":"outra","valor":99}""", id);

        (await Row(id)).Fatura.ShouldBe(invoice);
    }

    [Fact]
    [Trait("AC", "004-25")]
    public async Task UpdateFailingAfterTheInvoiceWasCreated_Returns500_AndLeavesNoInvoice()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);
        await _data.ExecuteAsync(
            """
            CREATE FUNCTION falha_put() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'segredo interno'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_falha_put BEFORE UPDATE ON transacoes FOR EACH ROW EXECUTE FUNCTION falha_put();
            """);
        try
        {
            var (status, body) = await Put($$"""{"conta_id":{{card}}}""");

            status.ShouldBe(HttpStatusCode.InternalServerError);
            body.ShouldBe(TransactionsHttp.Error("Erro ao atualizar transação"));
            (await _data.ScalarAsync<int>("SELECT count(*)::int FROM faturas")).ShouldBe(0);
        }
        finally
        {
            await _data.ExecuteAsync("DROP TRIGGER trg_falha_put ON transacoes; DROP FUNCTION falha_put();");
        }
    }

    [Fact]
    [Trait("AC", "004-84")]
    public async Task HugeId_Returns404Json()
    {
        var (status, body) = await _http.SendAsync(HttpMethod.Put, "/api/transactions/99999999999999999999", """{"descricao":"x"}""", _user);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Transação não encontrada"));
    }
}
