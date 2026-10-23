using System.Net;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using static MeuSecretario.IntegrationTests.Transactions.TransactionsTestData;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary>Autenticação nas 4 rotas (AC-004-80) e o <c>GET /api/transactions</c> (spec 004 §3.1), corpo como texto exato.</summary>
[Trait("Category", "Docker")]
public class TransactionsAuthAndGetEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
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

    public static TheoryData<string, string> Operations => new()
    {
        { "GET", "/api/transactions" },
        { "POST", "/api/transactions" },
        { "PUT", "/api/transactions/1" },
        { "DELETE", "/api/transactions/1" },
    };

    private Task<(HttpStatusCode Status, string Body)> Call(string method, string path, string? authorization) =>
        _http.SendAsync(new HttpMethod(method), path, json: method is "POST" or "PUT" ? "{}" : null, authorization: authorization);

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "004-80")]
    public async Task WithoutAuthorizationHeader_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, null);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(TransactionsHttp.Error("Token de autenticação não fornecido"));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "004-80")]
    public async Task WrongFormat_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, "Token abc");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(TransactionsHttp.Error("Formato de autenticação inválido. Use: Bearer <token>"));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "004-80")]
    public async Task ExpiredOrTamperedToken_Returns401(string method, string path)
    {
        foreach (var fixture in new[] { "expired", "tampered_payload", "wrong_secret" })
        {
            var (status, body) = await Call(method, path, "Bearer " + AuthFixtures.JwtToken(fixture));

            status.ShouldBe(HttpStatusCode.Unauthorized, fixture);
            body.ShouldBe(TransactionsHttp.Error("Token inválido ou expirado"), fixture);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "004-80")]
    public async Task TokenWithoutUserId_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, "Bearer " + AuthFixtures.JwtToken("no_user_id"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(TransactionsHttp.Error("Token não contém user_id"));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "004-80")]
    public async Task Authentication_ComesBeforeAnyValidation(string method, string path)
    {
        var (status, body) = await _http.SendAsync(new HttpMethod(method), path + (method == "GET" ? "?limit=0" : string.Empty), json: "{}");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(TransactionsHttp.Error("Token de autenticação não fornecido"));
    }

    // ---------- GET ----------

    [Fact]
    [Trait("AC", "004-01")]
    public async Task Get_ReturnsTheExactBody_WithKeysInOrder()
    {
        var id = await _data.InsertTransactionAsync(_user, _account, "Despesa", -70.55m, "2025-12-11", "Supermercado");

        var (status, body) = await _http.Get(_user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$$"""{"status":"success","data":{"total":1,"limit":20,"offset":0,"transactions":[{"id":{{{id}}},"descricao":"Supermercado","valor":-70.55,"tipo":"Despesa","data_transacao":"2025-12-11","categoria":"Despesas Gerais","subcategoria":"Outros","conta":"Corrente","consolidada":true,"conta_id":{{{_account}}},"subcategoria_id":59,"observacoes":null,"data":"2025-12-11","conta_bancaria":"Corrente","conta_bancaria_id":{{{_account}}}}]}}""");
    }

    [Fact]
    [Trait("AC", "004-09")]
    public async Task Get_WithoutTransactions_ReturnsAnEmptyList_AndIgnoresUnknownParameters()
    {
        var (status, body) = await _http.Get(_user, "?categoria=x");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":{"total":0,"limit":20,"offset":0,"transactions":[]}}""");
    }

    [Theory]
    [Trait("AC", "004-02")]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    [InlineData("?limit=-1")]
    [InlineData("?limit=99999999999999999999")]
    public async Task Get_LimitOutOfRange_Returns400(string query)
    {
        var (status, body) = await _http.Get(_user, query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(TransactionsHttp.Error("Parâmetro 'limit' deve estar entre 1 e 100"));
    }

    [Theory]
    [Trait("AC", "004-02")]
    [InlineData("?limit=abc", 20)]
    [InlineData("?limit=", 20)]
    [InlineData("?limit=1.5", 20)]
    [InlineData("?limit=%205", 5)]
    [InlineData("?limit=100", 100)]
    [InlineData("?limit=3&limit=7", 3)]
    public async Task Get_LimitValues_EchoTheEffectiveOne(string query, int expected)
    {
        var (status, body) = await _http.Get(_user, query);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain($"\"limit\":{expected},");
    }

    [Fact]
    [Trait("AC", "004-03")]
    public async Task Get_Offset_ErrorsAndFallbacks()
    {
        var (status, body) = await _http.Get(_user, "?offset=-1");
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(TransactionsHttp.Error("Parâmetro 'offset' deve ser maior ou igual a 0"));

        (await _http.Get(_user, "?offset=abc")).Body.ShouldContain("\"offset\":0,");
        (await _http.Get(_user, "?limit=0&offset=-1")).Body.ShouldBe(TransactionsHttp.Error("Parâmetro 'limit' deve estar entre 1 e 100"));
    }

    [Theory]
    [Trait("AC", "004-04")]
    [InlineData("?tipo=Renda")]
    [InlineData("?tipo=receita")]
    [InlineData("?tipo=Transfer%C3%AAncia")]
    public async Task Get_InvalidType_Returns400(string query)
    {
        var (status, body) = await _http.Get(_user, query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(TransactionsHttp.Error("Parâmetro 'tipo' deve ser 'Receita' ou 'Despesa'"));
    }

    [Fact]
    [Trait("AC", "004-04")]
    public async Task Get_TypeFilter_MapsReceitaToRenda_AndOutputsReceita()
    {
        await _data.InsertTransactionAsync(_user, _account, "Renda", 3000m, descricao: "salário", subcategoryId: IncomeSubcategory);
        await _data.InsertTransactionAsync(_user, _account, "Despesa", -10m, descricao: "pão");
        await _data.InsertTransactionAsync(_user, _account, "Transferência", -5m, descricao: "transf");
        await _data.InsertTransactionAsync(_user, _account, "Pagamento Fatura", -7m, descricao: "fatura");

        var income = (await _http.Get(_user, "?tipo=Receita")).Body;
        var expense = (await _http.Get(_user, "?tipo=Despesa")).Body;
        var all = (await _http.Get(_user, "?tipo=")).Body;

        income.ShouldContain("\"tipo\":\"Receita\"");
        income.ShouldContain("salário");
        income.ShouldNotContain("pão");
        income.ShouldContain("\"total\":1,");
        expense.ShouldContain("pão");
        expense.ShouldNotContain("salário");
        all.ShouldContain("\"total\":4,");
        all.ShouldContain("\"tipo\":\"Transferência\"");
        all.ShouldContain("\"tipo\":\"Pagamento Fatura\"");
        all.ShouldNotContain("\"tipo\":\"Renda\"");
    }

    [Fact]
    [Trait("AC", "004-05")]
    public async Task Get_DateFilters_AreInclusive()
    {
        foreach (var day in new[] { "2025-11-30", "2025-12-01", "2025-12-31", "2026-01-01" })
        {
            await _data.InsertTransactionAsync(_user, _account, data: day);
        }

        (await _http.Get(_user, "?data_inicio=2025-12-01&data_fim=2025-12-31")).Body.ShouldContain("\"total\":2,");
        (await _http.Get(_user, "?data_inicio=2025-12-31")).Body.ShouldContain("\"total\":2,");
        (await _http.Get(_user, "?data_fim=2025-12-01")).Body.ShouldContain("\"total\":2,");
        (await _http.Get(_user, "?data_inicio=2025-12-31&data_fim=2025-12-01")).Body.ShouldContain("\"total\":0,");
        (await _http.Get(_user, "?data_inicio=2025-1-5")).Status.ShouldBe(HttpStatusCode.OK);
        (await _http.Get(_user, "?data_inicio=")).Body.ShouldContain("\"total\":4,");
    }

    [Theory]
    [Trait("AC", "004-06")]
    [InlineData("?data_inicio=2025-13-01")]
    [InlineData("?data_inicio=2025-02-30")]
    [InlineData("?data_fim=01/12/2025")]
    [InlineData("?data_fim=2025-12-01T10:00")]
    [InlineData("?data_inicio=abc")]
    public async Task Get_InvalidDates_Return400(string query)
    {
        var (status, body) = await _http.Get(_user, query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(TransactionsHttp.Error("Datas devem estar no formato YYYY-MM-DD"));
    }

    [Fact]
    [Trait("AC", "004-06")]
    public async Task Get_ErrorOrder_IsLimitOffsetTypeDates()
    {
        (await _http.Get(_user, "?limit=0&offset=-1&tipo=x&data_inicio=abc")).Body
            .ShouldBe(TransactionsHttp.Error("Parâmetro 'limit' deve estar entre 1 e 100"));
        (await _http.Get(_user, "?offset=-1&tipo=x&data_inicio=abc")).Body
            .ShouldBe(TransactionsHttp.Error("Parâmetro 'offset' deve ser maior ou igual a 0"));
        (await _http.Get(_user, "?tipo=x&data_inicio=abc")).Body
            .ShouldBe(TransactionsHttp.Error("Parâmetro 'tipo' deve ser 'Receita' ou 'Despesa'"));
    }

    [Fact]
    [Trait("AC", "004-07")]
    public async Task Get_Pagination_UsesTheOrderAndTheRealTotal()
    {
        var ids = new List<int>();
        for (var day = 1; day <= 5; day++)
        {
            ids.Add(await _data.InsertTransactionAsync(_user, _account, data: $"2026-03-0{day}", descricao: $"d{day}"));
        }

        var (_, body) = await _http.Get(_user, "?limit=2&offset=2");

        body.ShouldContain("\"total\":5,\"limit\":2,\"offset\":2,");
        body.ShouldContain("\"descricao\":\"d3\"");
        body.ShouldContain("\"descricao\":\"d2\"");
        body.ShouldNotContain("\"descricao\":\"d4\"");
        (await _http.Get(_user, "?limit=2&offset=50")).Body
            .ShouldBe("""{"status":"success","data":{"total":5,"limit":2,"offset":50,"transactions":[]}}""");
    }

    [Fact]
    [Trait("AC", "004-08")]
    public async Task Get_NeverShowsAnotherUsersTransactions()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        await _data.InsertTransactionAsync(other, otherAccount, descricao: "segredo-do-outro");
        await _data.InsertTransactionAsync(_user, _account, descricao: "minha");

        var (_, body) = await _http.Get(_user);

        body.ShouldContain("minha");
        body.ShouldNotContain("segredo-do-outro");
        body.ShouldContain("\"total\":1,");
    }
}
