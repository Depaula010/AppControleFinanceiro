using System.Globalization;
using System.Net;
using MeuSecretario.Domain.Common;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;

namespace MeuSecretario.IntegrationTests.Bills;

/// <summary><c>RequireUser</c> nas 9 operações (AC-005-80), <c>GET /api/bills</c> (§3.1) e <c>GET /api/bills/summary</c> (§3.5).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class BillsGetAndSummaryEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly BillsHttp _http = new(postgres);
    private readonly BillsTestData _data = new(postgres.DataSource);
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _data.InsertUserAsync();
        _account = await _data.InsertAccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static TheoryData<string, string> Operations => new()
    {
        { "GET", "/api/bills" },
        { "POST", "/api/bills" },
        { "PUT", "/api/bills/1" },
        { "DELETE", "/api/bills/1" },
        { "GET", "/api/bills/summary" },
        { "GET", "/api/contas-mensais" },
        { "POST", "/api/contas-mensais" },
        { "PUT", "/api/contas-mensais/1" },
        { "DELETE", "/api/contas-mensais/1" },
    };

    private Task<(HttpStatusCode Status, string Body)> Call(string method, string path, string? authorization) =>
        _http.SendAsync(new HttpMethod(method), path, json: method is "POST" or "PUT" ? "{}" : null, authorization: authorization);

    // ============================== autenticação ==============================

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "005-80")]
    public async Task WithoutAuthorizationHeader_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, authorization: null);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(BillsHttp.Error("Token de autenticação não fornecido"));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "005-80")]
    public async Task WrongAuthorizationFormat_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, "Token abc");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(BillsHttp.Error("Formato de autenticação inválido. Use: Bearer <token>"));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "005-80")]
    public async Task ExpiredOrTamperedToken_Returns401(string method, string path)
    {
        foreach (var fixture in new[] { "expired", "tampered_payload", "wrong_secret" })
        {
            var (status, body) = await Call(method, path, "Bearer " + AuthFixtures.JwtToken(fixture));

            status.ShouldBe(HttpStatusCode.Unauthorized, fixture);
            body.ShouldBe(BillsHttp.Error("Token inválido ou expirado"), fixture);
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    [Trait("AC", "005-80")]
    public async Task TokenWithoutUserId_Returns401(string method, string path)
    {
        var (status, body) = await Call(method, path, "Bearer " + AuthFixtures.JwtToken("no_user_id"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(BillsHttp.Error("Token não contém user_id"));
    }

    // ============================== GET ==============================

    [Fact]
    [Trait("AC", "005-01")]
    [Trait("AC", "005-06")]
    public async Task Get_ReturnsTheExactBody_WithNineteenKeysInOrder()
    {
        var id = await _data.InsertScheduleAsync(_user, _account, "Aluguel", valor: 1500.5m, incluirNaReserva: true, dia: 5);

        var (status, body) = await _http.Get(_user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$"""{"status":"success","data":[{"id":{{id}},"descricao":"Aluguel","valor_previsto":1500.5,"tipo_agendamento":"FIXO","tipo_transacao":"Despesa","grupo":"Geral","periodicidade":"MENSAL","dia_execucao":5,"mes_execucao":null,"notificar_antes_dias":3,"subcategoria_id":59,"subcategoria_nome":"Outros","macro_categoria_nome":"Despesas Gerais","conta_id":{{_account}},"conta_nome":"Mercado pago","data_inicio":"2026-01-05","incluir_na_reserva":true,"total_parcelas":null,"parcelas_executadas":0}]}""");
    }

    [Fact]
    [Trait("AC", "005-07")]
    public async Task Get_IncomeGroupAndReceitasGerais_AreReceita()
    {
        var renda = await _data.GlobalSubcategoryOfGroupAsync("Renda");
        await _data.InsertScheduleAsync(_user, _account, "Salário", subcategoryId: renda, dia: 1);
        await _data.InsertScheduleAsync(_user, _account, "Freela", subcategoryId: BillsTestData.GeneralIncomeSubcategory, dia: 2);
        await _data.InsertScheduleAsync(_user, _account, "Luz", dia: 3);

        var (_, body) = await _http.Get(_user);

        body.ShouldContain("\"descricao\":\"Salário\",\"valor_previsto\":100,\"tipo_agendamento\":\"FIXO\",\"tipo_transacao\":\"Receita\",\"grupo\":\"Renda\"");
        body.ShouldContain("\"descricao\":\"Freela\",\"valor_previsto\":100,\"tipo_agendamento\":\"FIXO\",\"tipo_transacao\":\"Receita\",\"grupo\":\"Geral\"");
        body.ShouldContain("\"descricao\":\"Luz\",\"valor_previsto\":100,\"tipo_agendamento\":\"FIXO\",\"tipo_transacao\":\"Despesa\"");
    }

    [Fact]
    [Trait("AC", "005-02")]
    public async Task Get_IsOrderedByPeriodicityThenDay()
    {
        await _data.InsertScheduleAsync(_user, _account, "semanal", periodicidade: "SEMANAL");
        await _data.InsertScheduleAsync(_user, _account, "mensal", periodicidade: "MENSAL");
        await _data.InsertScheduleAsync(_user, _account, "anual", periodicidade: "ANUAL", mes: 3);
        await _data.InsertScheduleAsync(_user, _account, "diaria", periodicidade: "DIARIA");

        var (_, body) = await _http.Get(_user);

        var names = System.Text.RegularExpressions.Regex.Matches(body, "\"descricao\":\"([^\"]+)\"").Select(m => m.Groups[1].Value);
        names.ShouldBe(["anual", "diaria", "mensal", "semanal"]);
    }

    [Fact]
    [Trait("AC", "005-05")]
    public async Task Get_WithoutBills_ReturnsEmptyData()
    {
        await _data.InsertScheduleAsync(_user, _account, ativo: false);

        var (status, body) = await _http.Get(_user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    [Trait("AC", "005-81")]
    public async Task Get_AliasReturnsTheSameBody()
    {
        await _data.InsertScheduleAsync(_user, _account);

        var main = await _http.Get(_user, BillsHttp.Base);
        var alias = await _http.Get(_user, BillsHttp.Alias);

        alias.ShouldBe(main);
    }

    // ============================== summary ==============================

    private static string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private async Task SeedSpecSummaryAsync(bool include)
    {
        var renda = await _data.GlobalSubcategoryOfGroupAsync("Renda");
        await _data.InsertScheduleAsync(_user, _account, "m", periodicidade: "MENSAL", valor: 100m, incluirNaReserva: include);
        await _data.InsertScheduleAsync(_user, _account, "s", periodicidade: "SEMANAL", valor: 10m, incluirNaReserva: include, tipo: "LEMBRETE_VARIAVEL");
        await _data.InsertScheduleAsync(_user, _account, "q", periodicidade: "QUINZENAL", valor: 50m, incluirNaReserva: include);
        await _data.InsertScheduleAsync(_user, _account, "d", periodicidade: "DIARIA", valor: 2m, incluirNaReserva: include);
        await _data.InsertScheduleAsync(_user, _account, "a", periodicidade: "ANUAL", mes: 3, valor: 1200m, incluirNaReserva: include);
        await _data.InsertScheduleAsync(_user, _account, "r", periodicidade: "MENSAL", valor: 3000m, subcategoryId: renda, incluirNaReserva: false);
    }

    [Fact]
    [Trait("AC", "005-90")]
    [Trait("AC", "005-98")]
    public async Task Summary_AppliesTheFactors_AndKeepsTheKeyOrder()
    {
        await SeedSpecSummaryAsync(include: false);

        var (status, body) = await _http.Get(_user, "/api/bills/summary");

        // Soma na ordem fixa da consulta (periodicidade, grupo): DIARIA, MENSAL, QUINZENAL, SEMANAL.
        var expenses = 0.0;
        expenses += 2 * 30.0;
        expenses += 100.0;
        expenses += 50 * 2.16;
        expenses += 10 * 4.33;
        status.ShouldBe(HttpStatusCode.OK);
        Math.Abs(expenses - 311.3).ShouldBeLessThan(1e-9);
        body.ShouldBe(
            $$$$"""{"status":"success","data":{"total_despesas_mensais":{{{{R(expenses)}}}},"total_despesas_anuais":1200,"total_receitas_mensais":3000,"reserva_emergencia":{"meta":{{{{R(PyRound.Round2(expenses * 6))}}}},"gasto_mensal_equivalente":{{{{R(PyRound.Round2(expenses * 6 / 6))}}}},"meses_configurados":6}}}""");
    }

    [Fact]
    [Trait("AC", "005-93")]
    public async Task Summary_ReserveGoal_FollowsTheSpecExample()
    {
        await SeedSpecSummaryAsync(include: true);

        var (_, body) = await _http.Get(_user, "/api/bills/summary");

        body.ShouldContain("\"reserva_emergencia\":{\"meta\":3020,\"gasto_mensal_equivalente\":503.33,\"meses_configurados\":6}");
    }

    [Fact]
    [Trait("AC", "005-94")]
    public async Task Summary_Installment_EntersTheTotalsButNotTheGoal()
    {
        await _data.InsertScheduleAsync(_user, _account, "parcelado", tipo: "PARCELADO", totalParcelas: 6, valor: 500m, incluirNaReserva: true);
        await _data.InsertScheduleAsync(_user, _account, "fixo", valor: 100m, incluirNaReserva: true);

        var (_, body) = await _http.Get(_user, "/api/bills/summary");

        body.ShouldContain("\"total_despesas_mensais\":600,");
        body.ShouldContain("\"meta\":600,\"gasto_mensal_equivalente\":100,");
    }

    [Fact]
    [Trait("AC", "005-95")]
    public async Task Summary_NothingMarked_FallsBackToMonthlyExpensesTimesMonths()
    {
        await _data.InsertScheduleAsync(_user, _account, valor: 1000m, incluirNaReserva: false);

        var (_, body) = await _http.Get(_user, "/api/bills/summary");

        body.ShouldBe("""{"status":"success","data":{"total_despesas_mensais":1000,"total_despesas_anuais":0,"total_receitas_mensais":0,"reserva_emergencia":{"meta":6000,"gasto_mensal_equivalente":1000,"meses_configurados":6}}}""");
    }

    [Fact]
    [Trait("AC", "005-95")]
    public async Task Summary_WithoutBills_IsAllZero()
    {
        var (status, body) = await _http.Get(_user, "/api/bills/summary");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":{"total_despesas_mensais":0,"total_despesas_anuais":0,"total_receitas_mensais":0,"reserva_emergencia":{"meta":0,"gasto_mensal_equivalente":0,"meses_configurados":6}}}""");
    }

    [Theory]
    [Trait("AC", "005-92")]
    [InlineData(12, 12)]
    [InlineData(0, 0)]
    public async Task Summary_UsesTheConfiguredMonths(int configured, int expected)
    {
        var user = await _data.InsertUserAsync("5511900000003", "Carla", mesesReserva: configured);
        var account = await _data.InsertAccountAsync(user, "Conta");
        await _data.InsertScheduleAsync(user, account, valor: 100m, incluirNaReserva: true);

        var (_, body) = await _http.Get(user, "/api/bills/summary");

        body.ShouldContain($"\"meses_configurados\":{expected}}}");
        if (expected == 0)
        {
            body.ShouldContain("\"meta\":0,\"gasto_mensal_equivalente\":0,");   // sem divisão por zero
        }
        else
        {
            body.ShouldContain("\"meta\":1200,\"gasto_mensal_equivalente\":100,");
        }
    }

    [Fact]
    [Trait("AC", "005-91")]
    public async Task Summary_AnnualIncome_IsInNoTotal_AndInactiveAndNullAreOut()
    {
        var renda = await _data.GlobalSubcategoryOfGroupAsync("Renda");
        await _data.InsertScheduleAsync(_user, _account, "13o", periodicidade: "ANUAL", mes: 12, valor: 500m, subcategoryId: renda);
        await _data.InsertScheduleAsync(_user, _account, "inativo", valor: 99m, ativo: false);
        await _data.InsertScheduleAsync(_user, _account, "nulo", tipo: "LEMBRETE_VARIAVEL", valor: null);

        var (_, body) = await _http.Get(_user, "/api/bills/summary");

        body.ShouldContain("\"total_despesas_mensais\":0,\"total_despesas_anuais\":0,\"total_receitas_mensais\":0,");
    }

    [Fact]
    [Trait("AC", "005-91")]
    public async Task Summary_ReceitasGerais_CountsAsIncome()
    {
        await _data.InsertScheduleAsync(_user, _account, "Freela", valor: 40m, subcategoryId: BillsTestData.GeneralIncomeSubcategory);

        var (_, body) = await _http.Get(_user, "/api/bills/summary");

        body.ShouldContain("\"total_despesas_mensais\":0,");
        body.ShouldContain("\"total_receitas_mensais\":40,");
    }

    [Fact]
    [Trait("AC", "005-99")]
    public async Task Summary_NothingOfAnotherUserEnters()
    {
        var other = await _data.InsertUserAsync("5511900000002", "Beto");
        var otherAccount = await _data.InsertAccountAsync(other, "Dele");
        await _data.InsertScheduleAsync(other, otherAccount, valor: 9999m, incluirNaReserva: true);
        await _data.InsertScheduleAsync(_user, _account, valor: 10m, incluirNaReserva: false);

        var (_, body) = await _http.Get(_user, "/api/bills/summary");

        body.ShouldContain("\"total_despesas_mensais\":10,");
        body.ShouldContain("\"meta\":60,");
    }
}
