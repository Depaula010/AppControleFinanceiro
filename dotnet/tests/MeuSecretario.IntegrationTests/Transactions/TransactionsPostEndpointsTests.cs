using System.Net;
using MeuSecretario.IntegrationTests.Support;
using static MeuSecretario.IntegrationTests.Transactions.TransactionsTestData;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary><c>POST /api/transactions</c> (spec 004 §3.2): uma linha da tabela de validações por teste, corpo como texto exato.</summary>
[Trait("Category", "Docker")]
public class TransactionsPostEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly TransactionsHttp _http = new(postgres);
    private readonly TransactionsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync("5511900000001", "Ana");
        _account = await _data.InsertAccountAsync(_user, "Corrente");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Corpo válido; cada par substitui (ou, com <c>null</c>, remove) uma chave. O valor é JSON cru.</summary>
    private string Body(params (string Key, string? Json)[] overrides)
    {
        var fields = new List<(string Key, string? Json)>
        {
            ("descricao", "\"Mercado\""), ("valor", "150.5"), ("tipo", "\"Despesa\""), ("data", "\"2025-12-11\""),
            ("subcategoria_id", ExpenseSubcategory.J()), ("conta_id", _account.J()),
        };
        foreach (var (key, json) in overrides)
        {
            fields.RemoveAll(f => f.Key == key);
            fields.Add((key, json));
        }
        return "{" + string.Join(",", fields.Where(f => f.Json is not null).Select(f => $"\"{f.Key}\":{f.Json}")) + "}";
    }

    private async Task AssertBadRequest(string? json, string message)
    {
        var (status, body) = await _http.Post(_user, json);

        status.ShouldBe(HttpStatusCode.BadRequest, body);
        body.ShouldBe(TransactionsHttp.Error(message));
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
    }

    private async Task<(int Id, decimal Valor, string Tipo, string? Origem, bool Consolidada, int? Fatura, string? Notes, string Desc, DateOnly Data)> Stored() =>
        (await _data.QueryAsync<(int, decimal, string, string?, bool, int?, string?, string, DateTime)>(
            "SELECT id, valor, tipo_transacao, origem, consolidada, fatura_id, observacoes, descricao, data_transacao FROM transacoes"))
        .Select(r => (r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, r.Item6, r.Item7, r.Item8, DateOnly.FromDateTime(r.Item9)))
        .Single();

    // ---------- sucesso ----------

    [Fact]
    [Trait("AC", "004-10")]
    public async Task Expense_Returns201WithTheExactBody_AndStoresIt()
    {
        var (status, body) = await _http.Post(_user, Body());

        var stored = await Stored();
        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldBe(
            $$$"""{"status":"success","message":"Transação criada com sucesso","data":{"id":{{{stored.Id}}},"descricao":"Mercado","valor":-150.5,"tipo":"Despesa","data":"2025-12-11"}}""");
        (stored.Valor, stored.Tipo, stored.Origem, stored.Consolidada, stored.Fatura, stored.Notes)
            .ShouldBe((-150.50m, "Despesa", "api", true, null, null));
        (await _data.ScalarAsync<int>("SELECT usuario_id FROM transacoes")).ShouldBe(_user);
    }

    [Fact]
    [Trait("AC", "004-11")]
    public async Task Income_IsStoredAsRenda_AndEchoesReceita_AndShowsUpInTheReceitaFilter()
    {
        var (status, body) = await _http.Post(_user, Body(("tipo", "\"Receita\""), ("valor", "3000"), ("subcategoria_id", IncomeSubcategory.J())));

        var stored = await Stored();
        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"valor\":3000,\"tipo\":\"Receita\"");
        (stored.Valor, stored.Tipo).ShouldBe((3000m, "Renda"));
        (await _http.Get(_user, "?tipo=Receita")).Body.ShouldContain("\"total\":1,");
    }

    [Theory]
    [Trait("AC", "004-12")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"abc\"")]
    [InlineData("{}")]
    public async Task InvalidBody_Returns400OnDescricao_NeverA500(string? json)
    {
        await AssertBadRequest(json, "Campo 'descricao' é obrigatório");
    }

    [Fact]
    [Trait("AC", "004-12")]
    public async Task BodyWithoutContentType_IsRead()
    {
        var (status, _) = await _http.Post(_user, Body(), contentType: "text/plain");

        status.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    [Trait("AC", "004-13")]
    public async Task TwoErrors_RespondTheFirstOfTheOrder()
    {
        await AssertBadRequest("{}", "Campo 'descricao' é obrigatório");
        await AssertBadRequest(Body(("valor", null), ("tipo", "\"x\"")), "Campo 'valor' é obrigatório");
        await AssertBadRequest(Body(("valor", "\"abc\""), ("tipo", "\"x\"")), "Valor inválido");
        await AssertBadRequest(Body(("valor", "-1"), ("tipo", "\"x\"")), "Valor deve ser positivo");
        await AssertBadRequest(Body(("data", null), ("conta_id", null)), "Campo 'data' é obrigatório");
        await AssertBadRequest(Body(("subcategoria_id", null), ("conta_id", null)), "Campo 'subcategoria_id' é obrigatório");
    }

    [Fact]
    [Trait("AC", "004-14")]
    public async Task Description_IsTrimmed_AndBlankIsRequired()
    {
        var (status, body) = await _http.Post(_user, Body(("descricao", "\"  Mercado  \"")));

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"descricao\":\"Mercado\"");
        (await Stored()).Desc.ShouldBe("Mercado");
        await _data.ExecuteAsync("DELETE FROM transacoes");

        await AssertBadRequest(Body(("descricao", "\"  \"")), "Campo 'descricao' é obrigatório");
        await AssertBadRequest(Body(("descricao", null)), "Campo 'descricao' é obrigatório");
    }

    [Fact]
    [Trait("AC", "004-15")]
    public async Task Value_Variants()
    {
        await AssertBadRequest(Body(("valor", "null")), "Campo 'valor' é obrigatório");
        await AssertBadRequest(Body(("valor", null)), "Campo 'valor' é obrigatório");
        foreach (var invalid in new[] { "\"1,5\"", "\"abc\"", "[]", "{}", "\"nan\"", "\"inf\"" })
        {
            await AssertBadRequest(Body(("valor", invalid)), "Valor inválido");
        }
        await AssertBadRequest(Body(("valor", "-0.01")), "Valor deve ser positivo");
        await AssertBadRequest(Body(("valor", "\"-5\"")), "Valor deve ser positivo");

        var (zeroStatus, zeroBody) = await _http.Post(_user, Body(("valor", "0")));
        zeroStatus.ShouldBe(HttpStatusCode.Created);
        zeroBody.ShouldContain("\"valor\":-0,");   // o Flask devolve -0.0
        foreach (var accepted in new[] { ("\"12.5\"", "-12.5"), ("\" 7 \"", "-7"), ("true", "-1") })
        {
            var (status, body) = await _http.Post(_user, Body(("valor", accepted.Item1)));
            status.ShouldBe(HttpStatusCode.Created);
            body.ShouldContain($"\"valor\":{accepted.Item2},");
        }
    }

    [Fact]
    [Trait("AC", "004-16")]
    public async Task ThreeDecimalPlaces_AreRoundedByTheDatabase_ButEchoedAsSent()
    {
        var (_, body) = await _http.Post(_user, Body(("valor", "10.005")));

        body.ShouldContain("\"valor\":-10.005,");
        (await Stored()).Valor.ShouldBe(-10.01m);
    }

    [Theory]
    [Trait("AC", "004-17")]
    [InlineData("\"receita\"")]
    [InlineData("\"Renda\"")]
    [InlineData("\"Transferência\"")]
    [InlineData("null")]
    public async Task InvalidType_Returns400(string tipo)
    {
        await AssertBadRequest(Body(("tipo", tipo)), "Tipo deve ser 'Receita' ou 'Despesa'");
        await AssertBadRequest(Body(("tipo", null)), "Tipo deve ser 'Receita' ou 'Despesa'");
    }

    [Fact]
    [Trait("AC", "004-18")]
    public async Task Date_Variants()
    {
        foreach (var missing in new[] { "null", "\"\"", "0", "false" })
        {
            await AssertBadRequest(Body(("data", missing)), "Campo 'data' é obrigatório");
        }
        await AssertBadRequest(Body(("data", null)), "Campo 'data' é obrigatório");
        foreach (var bad in new[] { "\"2025-13-01\"", "\"2025-02-30\"", "\"11/12/2025\"", "\"2025-12-11T10:00\"" })
        {
            await AssertBadRequest(Body(("data", bad)), "Data deve estar no formato YYYY-MM-DD");
        }

        var (status, body) = await _http.Post(_user, Body(("data", "\"2025-1-5\"")));
        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"data\":\"2025-1-5\"}");
        (await Stored()).Data.ShouldBe(Day("2025-01-05"));
    }

    [Fact]
    [Trait("AC", "004-19")]
    public async Task FalsyIds_AreRequired()
    {
        foreach (var falsy in new[] { "null", "0", "\"\"", "false" })
        {
            await AssertBadRequest(Body(("subcategoria_id", falsy)), "Campo 'subcategoria_id' é obrigatório");
            await AssertBadRequest(Body(("conta_id", falsy)), "Campo 'conta_id' é obrigatório");
        }
    }

    [Fact]
    [Trait("AC", "004-20")]
    public async Task UnknownForeignAndInactiveAccounts_Return404_AndNothingIsWritten()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreign = await _data.InsertAccountAsync(other, "Alheia");
        var inactive = await _data.InsertAccountAsync(_user, "Velha", ativa: false);

        foreach (var id in new[] { 99999, foreign, inactive })
        {
            var (status, body) = await _http.Post(_user, Body(("conta_id", id.J())));

            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(TransactionsHttp.Error("Conta não encontrada ou não pertence ao usuário"));
        }
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-20")]
    public async Task UnknownAndForeignSubcategories_Return404_OwnAndGlobalAreAccepted_AccountComesFirst()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var foreignSub = await _data.ScalarAsync<int>(
            "INSERT INTO subcategoria (macro_id, nome_sub, usuario_id) VALUES (17, 'Dele', @other) RETURNING id", new { other });
        var ownSub = await _data.ScalarAsync<int>(
            "INSERT INTO subcategoria (macro_id, nome_sub, usuario_id) VALUES (17, 'Minha', @_user) RETURNING id", new { _user });

        foreach (var id in new[] { 99999, foreignSub })
        {
            var (status, body) = await _http.Post(_user, Body(("subcategoria_id", id.J())));
            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe(TransactionsHttp.Error("Subcategoria não encontrada"));
        }
        (await _http.Post(_user, Body(("subcategoria_id", ownSub.J())))).Status.ShouldBe(HttpStatusCode.Created);
        (await _http.Post(_user, Body())).Status.ShouldBe(HttpStatusCode.Created);

        var both = await _http.Post(_user, Body(("subcategoria_id", "99999"), ("conta_id", "99999")));
        both.Body.ShouldBe(TransactionsHttp.Error("Conta não encontrada ou não pertence ao usuário"));
    }

    [Fact]
    [Trait("AC", "004-21")]
    public async Task Notes_AreTrimmed_AndBlankIsNull()
    {
        await _http.Post(_user, Body(("observacoes", "\"  Compras  \"")));
        (await Stored()).Notes.ShouldBe("Compras");
        await _data.ExecuteAsync("DELETE FROM transacoes");

        foreach (var blank in new[] { "\"\"", "\"   \"" })
        {
            await _http.Post(_user, Body(("observacoes", blank)));
        }
        await _http.Post(_user, Body());
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes WHERE observacoes IS NULL")).ShouldBe(3);
    }

    [Fact]
    [Trait("AC", "004-22")]
    public async Task Consolidated_DefaultsToTrue_AndFalseIsStored()
    {
        await _http.Post(_user, Body(("consolidada", "false")));
        (await Stored()).Consolidada.ShouldBeFalse();
        await _data.ExecuteAsync("DELETE FROM transacoes");

        await _http.Post(_user, Body());
        (await Stored()).Consolidada.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-27")]
    public async Task UnknownKeys_AreIgnored_OwnerIsTheTokenUser()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");

        var (status, _) = await _http.Post(_user, Body(("usuario_id", other.J()), ("origem", "\"x\""), ("fatura_id", "9"), ("id", "1")));

        status.ShouldBe(HttpStatusCode.Created);
        var stored = await Stored();
        (stored.Origem, stored.Fatura).ShouldBe(("api", null));
        (await _data.ScalarAsync<int>("SELECT usuario_id FROM transacoes")).ShouldBe(_user);
    }

    [Fact]
    [Trait("AC", "004-28")]
    public async Task AnotherUsersAccountId_Is404_AndOnlyConsolidatedCountsForTheBalance()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        (await _http.Post(other, Body())).Status.ShouldBe(HttpStatusCode.NotFound);

        await _http.Post(_user, Body(("consolidada", "true"), ("valor", "100")));
        await _http.Post(_user, Body(("consolidada", "false"), ("valor", "40")));
        (await _http.SendAsync(HttpMethod.Get, "/api/accounts", userId: _user)).Body.ShouldContain("\"saldo\":-100,");
    }

    // ---------- fatura ----------

    private static string Iso(DateTime value) => DateOnly.FromDateTime(value).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<(int Id, string Due, string Closing, string Status)[]> Invoices() =>
        (await _data.QueryAsync<(int, DateTime, DateTime, string)>(
            "SELECT id, data_vencimento, data_fechamento, status FROM faturas ORDER BY id"))
        .Select(r => (r.Item1, Iso(r.Item2), Iso(r.Item3), r.Item4))
        .ToArray();

    [Fact]
    [Trait("AC", "004-23")]
    public async Task ExpenseOnACard_CreatesTheInvoice_AndASecondPurchaseReusesIt()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);

        (await _http.Post(_user, Body(("conta_id", card.J()), ("data", "\"2026-03-20\"")))).Status.ShouldBe(HttpStatusCode.Created);
        (await _http.Post(_user, Body(("conta_id", card.J()), ("data", "\"2026-03-25\"")))).Status.ShouldBe(HttpStatusCode.Created);

        var invoice = (await Invoices()).Single();
        (invoice.Due, invoice.Closing, invoice.Status).ShouldBe(("2026-04-15", "2026-04-10", "Aberta"));
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes WHERE fatura_id = @id", new { id = invoice.Id })).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "004-24")]
    public async Task IncomeOnACard_ExpenseOnANonCard_AndCardWithoutDays_HaveNoInvoice()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);
        var noDays = await _data.Accounts.InsertAccountAsync(_user, "Cartão cru", "Cartão de Crédito");

        await _http.Post(_user, Body(("conta_id", card.J()), ("tipo", "\"Receita\"")));
        await _http.Post(_user, Body(("conta_id", _account.J())));
        await _http.Post(_user, Body(("conta_id", noDays.J())));

        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM faturas")).ShouldBe(0);
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM transacoes WHERE fatura_id IS NULL")).ShouldBe(3);
    }

    [Fact]
    [Trait("AC", "004-25")]
    public async Task InsertFailingAfterTheInvoice_Returns500_AndLeavesNoInvoice()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);
        await _data.ExecuteAsync(
            """
            CREATE FUNCTION falha_post() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'segredo interno'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER trg_falha_post BEFORE INSERT ON transacoes FOR EACH ROW EXECUTE FUNCTION falha_post();
            """);
        try
        {
            var (status, body) = await _http.Post(_user, Body(("conta_id", card.J())));

            status.ShouldBe(HttpStatusCode.InternalServerError);
            body.ShouldBe(TransactionsHttp.Error("Erro ao criar transação"));
            (await _data.ScalarAsync<int>("SELECT count(*)::int FROM faturas")).ShouldBe(0);
        }
        finally
        {
            await _data.ExecuteAsync("DROP TRIGGER trg_falha_post ON transacoes; DROP FUNCTION falha_post();");
        }
    }

    [Fact]
    [Trait("AC", "004-26")]
    public async Task SimultaneousPurchasesOnTheSameNewCycle_ShareOneInvoice()
    {
        var card = await _data.InsertCardAsync(_user, "Nubank", 10, 15);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            _http.Post(_user, Body(("conta_id", card.J()), ("data", "\"2026-03-20\"")))));

        responses.ShouldAllBe(r => r.Status == HttpStatusCode.Created);
        (await _data.ScalarAsync<int>("SELECT count(*)::int FROM faturas")).ShouldBe(1);
    }

    // ---------- corpo do Angular ----------

    [Fact]
    [Trait("AC", "004-29")]
    public async Task TheBodyTheAngularSends_Returns201()
    {
        var json = $$"""{"descricao":"Almoço","valor":32.9,"tipo":"Despesa","data":"2026-03-10","subcategoria_id":{{ExpenseSubcategory}},"conta_id":{{_account}},"consolidada":true}""";

        var (status, body) = await _http.Post(_user, json);

        status.ShouldBe(HttpStatusCode.Created);
        body.ShouldContain("\"valor\":-32.9,");
    }

    // ---------- desvio 066 ----------

    [Fact]
    [Trait("AC", "004-12")]
    public async Task FormerlyA500_NowA400WithTheMessage()
    {
        await AssertBadRequest(Body(("descricao", "5")), "Campo 'descricao' é obrigatório");
        await AssertBadRequest(Body(("descricao", $"\"{new string('a', 256)}\"")), "Campo 'descricao' deve ter no máximo 255 caracteres");
        await AssertBadRequest(Body(("observacoes", "5")), "Campo 'observacoes' inválido");
        await AssertBadRequest(Body(("consolidada", "\"sim\"")), "Campo 'consolidada' inválido");
        await AssertBadRequest(Body(("data", "5")), "Data deve estar no formato YYYY-MM-DD");
        await AssertBadRequest(Body(("valor", "\"1e20\"")), "Valor inválido");

        var (status, body) = await _http.Post(_user, Body(("conta_id", "\"abc\"")));
        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe(TransactionsHttp.Error("Conta não encontrada ou não pertence ao usuário"));
    }
}
