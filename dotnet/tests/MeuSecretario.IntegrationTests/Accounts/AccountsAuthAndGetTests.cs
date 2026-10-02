using System.Net;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;

namespace MeuSecretario.IntegrationTests.Accounts;

/// <summary>
/// Primeira prova do filtro <c>RequireUser</c> numa rota <c>/api/*</c> real (AC-001-40 a 44, AC-002-80 e 81),
/// nas 8 operações, e o <c>GET</c> de contas (spec 002 §3.1).
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class AccountsAuthAndGetTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly AccountsHttp _http = new(postgres);
    private readonly AccountsTestData _data = new(postgres.DataSource);

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> Operations => new()
    {
        { "GET", "/api/accounts" },
        { "POST", "/api/accounts" },
        { "PUT", "/api/accounts/1" },
        { "DELETE", "/api/accounts/1" },
        { "GET", "/api/contas" },
        { "POST", "/api/contas" },
        { "PUT", "/api/contas/1" },
        { "DELETE", "/api/contas/1" },
    };

    private Task<(HttpStatusCode Status, string Body)> Call(string method, string path, string? authorization) =>
        _http.SendAsync(new HttpMethod(method), path, json: method is "POST" or "PUT" ? "{}" : null, authorization: authorization);

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "002-80")]
    [Trait("AC", "001-40")]
    public async Task WithoutAuthorizationHeader_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, authorization: null);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(AccountsHttp.Error("Token de autenticação não fornecido"));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "002-80")]
    [Trait("AC", "001-41")]
    public async Task WrongAuthorizationFormat_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, "Token abc");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(AccountsHttp.Error("Formato de autenticação inválido. Use: Bearer <token>"));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "002-80")]
    [Trait("AC", "001-42")]
    public async Task ExpiredOrTamperedToken_Returns401(string method, string path)
    {
        foreach (var fixture in new[] { "expired", "tampered_payload", "wrong_secret" })
        {
            var (status, body) = await Call(method, path, "Bearer " + AuthFixtures.JwtToken(fixture));

            status.ShouldBe(HttpStatusCode.Unauthorized, fixture);
            body.ShouldBe(AccountsHttp.Error("Token inválido ou expirado"), fixture);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "002-80")]
    [Trait("AC", "001-43")]
    public async Task TokenWithoutUserId_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, "Bearer " + AuthFixtures.JwtToken("no_user_id"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(AccountsHttp.Error("Token não contém user_id"));
    }

    [Fact]
    [Trait("AC", "001-44")]
    public async Task ValidFixtureToken_ReachesTheEndpoint_WithTheTokenUser()
    {
        await _data.InsertUserWithIdAsync(AuthFixtures.JwtUserId);
        await _data.InsertAccountAsync(AuthFixtures.JwtUserId, "Carteira");

        var (status, body) = await Call("GET", "/api/accounts", "Bearer " + AuthFixtures.JwtToken("valid"));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"nome\":\"Carteira\"");
    }

    // ============================== GET ==============================

    [Fact]
    [Trait("AC", "002-01")]
    [Trait("AC", "002-06")]
    public async Task Get_ReturnsTheExactBody_WithOriginalKeysThenExtras()
    {
        var user = await _data.InsertUserAsync();
        var id = await _data.InsertAccountAsync(user, "Carteira", "Dinheiro", 100m);
        await _data.InsertTransactionAsync(user, id, -30m);
        await _data.InsertTransactionAsync(user, id, 0.55m);
        await _data.InsertTransactionAsync(user, id, -50m, consolidada: false);

        var (status, body) = await _http.Get(user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$"""{"status":"success","data":[{"id":{{id}},"nome":"Carteira","banco":"Dinheiro","tipo":"Dinheiro","saldo":70.55,"nome_conta":"Carteira","tipo_conta":"Dinheiro","saldo_inicial":100,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":null,"dia_fechamento":null,"dia_vencimento":null}]}""");
    }

    [Fact]
    [Trait("AC", "002-07")]
    public async Task Get_ReturnsCardFieldsAndZeroLimitAsNull()
    {
        var user = await _data.InsertUserAsync();
        var card = await _data.InsertAccountAsync(user, "Nubank", "Cartão de Crédito", 0m, fechamento: 5, vencimento: 10, cor: "#112233", icone: "card", limite: 5000m);
        var zero = await _data.InsertAccountAsync(user, "Zerado", "Conta Corrente", limite: 0m);

        var (_, body) = await _http.Get(user);

        body.ShouldContain($$"""{"id":{{card}},"nome":"Nubank","banco":"Cartão de Crédito","tipo":"Cartão de Crédito","saldo":0,"nome_conta":"Nubank","tipo_conta":"Cartão de Crédito","saldo_inicial":0,"cor_hex":"#112233","icone":"card","limite_credito":5000,"dia_fechamento":5,"dia_vencimento":10}""");
        body.ShouldContain($$"""{"id":{{zero}},"nome":"Zerado","banco":"Conta Corrente","tipo":"Conta Corrente","saldo":0,"nome_conta":"Zerado","tipo_conta":"Conta Corrente","saldo_inicial":0,"cor_hex":"#3B82F6","icone":"wallet","limite_credito":null""");
    }

    [Fact]
    [Trait("AC", "002-02")]
    public async Task Get_OrdersByTypeThenName()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Zeta", "Dinheiro");
        await _data.InsertAccountAsync(user, "Beta", "Dinheiro");
        await _data.InsertAccountAsync(user, "Alfa", "Conta Corrente");

        var (_, body) = await _http.Get(user);

        var names = System.Text.RegularExpressions.Regex.Matches(body, "\"nome\":\"([^\"]+)\"").Select(m => m.Groups[1].Value);
        names.ShouldBe(["Alfa", "Beta", "Zeta"]);
    }

    [Fact]
    [Trait("AC", "002-05")]
    public async Task Get_WithoutActiveAccounts_ReturnsEmptyData()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Inativa", ativa: false);

        var (status, body) = await _http.Get(user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    [Trait("AC", "002-04")]
    [Trait("AC", "002-81")]
    public async Task Get_NeverShowsOtherUsersOrInactiveAccounts()
    {
        var a = await _data.InsertUserAsync("5511900000001");
        var b = await _data.InsertUserAsync("5511900000002");
        await _data.InsertAccountAsync(a, "ContaDoA");
        await _data.InsertAccountAsync(b, "ContaDoB");
        await _data.InsertAccountAsync(b, "InativaDoB", ativa: false);

        var (_, forB) = await _http.Get(b);
        var (_, forA) = await _http.Get(a);

        forB.ShouldContain("ContaDoB");
        forB.ShouldNotContain("ContaDoA");
        forB.ShouldNotContain("InativaDoB");
        forA.ShouldContain("ContaDoA");
        forA.ShouldNotContain("ContaDoB");
    }

    [Fact]
    [Trait("AC", "002-82")]
    public async Task Get_AliasReturnsTheSameBody()
    {
        var user = await _data.InsertUserAsync();
        await _data.InsertAccountAsync(user, "Carteira");

        var main = await _http.Get(user, "/api/accounts");
        var alias = await _http.Get(user, "/api/contas");

        alias.ShouldBe(main);
    }
}
