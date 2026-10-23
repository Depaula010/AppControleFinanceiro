using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Dashboard;
using MeuSecretario.Api.Dashboard;
using MeuSecretario.Domain.Dashboard;
using MeuSecretario.IntegrationTests.Accounts;
using MeuSecretario.IntegrationTests.Bills;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Dashboard;

/// <summary>Apoio dos testes da 006: dados das features 004 e 005, relógio fixo em 2026-10-15 (quinta-feira) e chamadas HTTP.</summary>
internal sealed class DashboardTestKit(PostgresFixture postgres)
{
    public static readonly DateOnly Today = new(2026, 10, 15);

    public TransactionsTestData Transactions { get; } = new(postgres.DataSource);

    public BillsTestData Bills { get; } = new(postgres.DataSource);

    public WebApplicationFactory<Program> Factory { get; } = new BillsHttp(postgres).WithClock(Today);

    public async Task<(HttpStatusCode Status, string Body)> SendAsync(
        string path, int? userId, string method = "GET", string? authorization = null)
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        if (userId is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + AccountsHttp.TokenFor(userId.Value));
        }
        else if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public static string Error(string message) => AccountsHttp.Error(message);

    public static string Ok(string data) => $$"""{"status":"success","data":{{data}}}""";
}

[Trait("Category", "Docker")]
public class DashboardEndpointsTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly DashboardTestKit _kit = new(postgres);
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _kit.Transactions.InsertUserAsync();
        _account = await _kit.Transactions.InsertAccountAsync(_user, "Corrente");
    }

    public async Task DisposeAsync() => await _kit.Factory.DisposeAsync();

    public static TheoryData<string> AllPaths => new()
    {
        "/api/dashboard/summary", "/api/dashboard/resumo", "/api/dashboard/stats", "/api/dashboard/charts",
        "/api/dashboard/recent", "/api/transactions/recent", "/api/transacoes/recentes", "/api/dashboard/alerts",
    };

    // ============================== autenticação e roteamento ==============================

    [Theory]
    [MemberData(nameof(AllPaths))]
    [Trait("AC", "006-01")]
    public async Task WithoutAuthorization_Returns401(string path)
    {
        var (status, body) = await _kit.SendAsync(path, userId: null);

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(DashboardTestKit.Error("Token de autenticação não fornecido"));
    }

    [Theory]
    [MemberData(nameof(AllPaths))]
    [Trait("AC", "006-01")]
    public async Task WrongFormatAndBadTokens_Return401(string path)
    {
        var (status, body) = await _kit.SendAsync(path, userId: null, authorization: "Token abc");
        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(DashboardTestKit.Error("Formato de autenticação inválido. Use: Bearer <token>"));

        foreach (var fixture in new[] { "expired", "tampered_payload", "wrong_secret" })
        {
            (status, body) = await _kit.SendAsync(path, userId: null, authorization: "Bearer " + AuthFixtures.JwtToken(fixture));
            status.ShouldBe(HttpStatusCode.Unauthorized, fixture);
            body.ShouldBe(DashboardTestKit.Error("Token inválido ou expirado"), fixture);
        }
    }

    [Fact]
    [Trait("AC", "006-01")]
    public async Task Charts_InvalidMeses_StillAnswers401BeforeValidating()
    {
        var (status, _) = await _kit.SendAsync("/api/dashboard/charts?meses=99", userId: null);

        status.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/dashboard/summary/")]
    [InlineData("/api/dashboard/charts/")]
    [InlineData("/api/dashboard/recent/")]
    [InlineData("/api/dashboard/alerts/")]
    [InlineData("/api/dashboard/xyz")]
    [InlineData("/api/dashboard")]
    [Trait("AC", "006-03")]
    public async Task TrailingSlashOrUnknownPath_Is404(string path)
    {
        var (status, _) = await _kit.SendAsync(path, _user);

        status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(AllPaths))]
    [Trait("AC", "006-04")]
    public async Task OtherMethods_Return405(string path)
    {
        foreach (var method in new[] { "POST", "PUT", "DELETE" })
        {
            var (status, body) = await _kit.SendAsync(path, _user, method);
            status.ShouldBe(HttpStatusCode.MethodNotAllowed, $"{method} {path} => {body}");
        }
    }

    [Fact]
    [Trait("AC", "006-02")]
    public async Task SummaryAliases_AreIdentical()
    {
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Renda", 10m, "2026-10-05");

        var main = await _kit.SendAsync("/api/dashboard/summary", _user);
        main.Status.ShouldBe(HttpStatusCode.OK);
        (await _kit.SendAsync("/api/dashboard/resumo", _user)).ShouldBe(main);
        (await _kit.SendAsync("/api/dashboard/stats", _user)).ShouldBe(main);
    }

    [Fact]
    [Trait("AC", "006-02")]
    public async Task RecentAliases_AreIdentical()
    {
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -10m, "2026-10-05");

        var main = await _kit.SendAsync("/api/dashboard/recent", _user);
        main.Status.ShouldBe(HttpStatusCode.OK);
        (await _kit.SendAsync("/api/transactions/recent", _user)).ShouldBe(main);
        (await _kit.SendAsync("/api/transacoes/recentes", _user)).ShouldBe(main);
    }

    // ============================== resumo ==============================

    [Fact]
    [Trait("AC", "006-10")]
    public async Task Summary_WithoutData_IsZero()
    {
        var other = await _kit.Transactions.InsertUserAsync("5511888888888", "Outro");

        var (status, body) = await _kit.SendAsync("/api/dashboard/summary", other);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(DashboardTestKit.Ok("""{"saldo_total":0,"receitas_mes":0,"despesas_mes":0,"saldo_mes":0,"mes_referencia":"Outubro/2026"}"""));
    }

    [Fact]
    [Trait("AC", "006-11")]
    [Trait("AC", "006-12")]
    [Trait("AC", "006-13")]
    [Trait("AC", "006-14")]
    [Trait("AC", "006-15")]
    [Trait("AC", "006-16")]
    [Trait("AC", "006-19")]
    [Trait("AC", "006-20")]
    public async Task Summary_AppliesEveryRuleOfTheFlask()
    {
        var data = _kit.Transactions;
        var wallet = await data.InsertAccountAsync(_user, "Carteira", "Dinheiro");
        // saldo_inicial: 100 na Corrente; Carteira 50 (dá para ajustar pelo SQL abaixo).
        await _kit.Bills.ExecuteAsync("UPDATE contas SET saldo_inicial = 100 WHERE id = @_account", new { _account });
        await _kit.Bills.ExecuteAsync("UPDATE contas SET saldo_inicial = 50 WHERE id = @wallet", new { wallet });
        var inactive = await data.Accounts.InsertAccountAsync(_user, "Antiga", "Dinheiro", saldoInicial: 999m, ativa: false);

        await data.InsertTransactionAsync(_user, _account, "Renda", 100m, "2026-10-05");
        await data.InsertTransactionAsync(_user, _account, "Despesa", -10m, "2026-10-06");
        await data.InsertTransactionAsync(_user, _account, "Despesa", -500m, "2026-10-07", consolidada: false);
        await data.InsertTransactionAsync(_user, wallet, "Transferência", -40m, "2026-10-08");
        await data.InsertTransactionAsync(_user, wallet, "Pagamento Fatura", -60m, "2026-10-09");
        await data.InsertTransactionAsync(_user, wallet, "Despesa", -7m, "2026-09-30");
        await data.InsertTransactionAsync(_user, wallet, "Despesa", -1000m, "2026-10-16");
        await data.InsertTransactionAsync(_user, inactive, "Despesa", -20m, "2026-10-10");

        var (status, body) = await _kit.SendAsync("/api/dashboard/summary", _user);

        // saldo: Corrente 100+100-10 = 190; Carteira 50-40-60-7-1000 = -1057; a conta inativa fica de fora.
        // mês: receitas 100; despesas 10 + 20 (a conta inativa conta); sem a não consolidada, a futura e o mês anterior.
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(DashboardTestKit.Ok("""{"saldo_total":-867,"receitas_mes":100,"despesas_mes":30,"saldo_mes":70,"mes_referencia":"Outubro/2026"}"""));
    }

    [Fact]
    [Trait("AC", "006-14")]
    public async Task Summary_MonthBoundaries_AreInclusive()
    {
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -1m, "2026-10-01");
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -2m, "2026-10-15");
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -4m, "2026-09-30");
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -8m, "2026-10-16");

        var (_, body) = await _kit.SendAsync("/api/dashboard/summary", _user);

        body.ShouldContain("\"despesas_mes\":3,");
    }

    [Fact]
    [Trait("AC", "006-20")]
    public async Task Summary_CardAccountEntersTheBalanceWithItsSign()
    {
        var card = await _kit.Transactions.InsertCardAsync(_user);
        await _kit.Transactions.InsertTransactionAsync(_user, card, "Despesa", -300m, "2026-10-05");

        var (_, body) = await _kit.SendAsync("/api/dashboard/summary", _user);

        body.ShouldContain("\"saldo_total\":-300,");
    }

    // ============================== gráficos ==============================

    private async Task SeedChartsAsync()
    {
        var data = _kit.Transactions;
        await data.InsertTransactionAsync(_user, _account, "Despesa", -100m, "2026-10-05");
        await data.InsertTransactionAsync(_user, _account, "Despesa", -50m, "2026-10-10");
        await data.InsertTransactionAsync(_user, _account, "Despesa", -200m, "2026-09-20");
        await data.InsertTransactionAsync(_user, _account, "Despesa", -30m, "2026-07-03");
        await data.InsertTransactionAsync(_user, _account, "Despesa", -999m, "2026-07-02");
        await data.InsertTransactionAsync(_user, _account, "Despesa", -5000m, "2026-10-11", consolidada: false);
        await data.InsertTransactionAsync(_user, _account, "Renda", 300m, "2026-10-12");
    }

    [Fact]
    [Trait("AC", "006-30")]
    [Trait("AC", "006-33")]
    [Trait("AC", "006-34")]
    [Trait("AC", "006-36")]
    [Trait("AC", "006-37")]
    [Trait("AC", "006-70")]
    public async Task Charts_Default_ReproducesTheBlocks_WithWholeMonthsInAscendingOrder()
    {
        await SeedChartsAsync();

        var (status, body) = await _kit.SendAsync("/api/dashboard/charts", _user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(DashboardTestKit.Ok(
            """{"gastos_mensais":[{"mes":"2026-09","total":200},{"mes":"2026-10","total":150}]""" +
            ""","gastos_categoria":[{"macro_categoria":"Despesas Gerais","subcategoria":"Outros","total":150,"quantidade":2}]""" +
            ""","gastos_dia_semana":[{"dia":"Sunday","total":200,"quantidade":1},{"dia":"Monday","total":100,"quantidade":1},{"dia":"Saturday","total":50,"quantidade":1}]}"""));
    }

    [Fact]
    [Trait("AC", "006-38")]
    public async Task Charts_MesesOnlyChangesTheMonthlyBlock()
    {
        await SeedChartsAsync();

        var (_, one) = await _kit.SendAsync("/api/dashboard/charts?meses=1", _user);
        var (_, twelve) = await _kit.SendAsync("/api/dashboard/charts?meses=12", _user);

        // Desvio 090: `meses=1` é só o mês atual; desvio 091: do mais antigo para o mais novo.
        Blocks(one).Monthly.ShouldBe(["2026-10"]);
        Blocks(twelve).Monthly.ShouldBe(["2026-07", "2026-09", "2026-10"]);
        Blocks(twelve).Others.ShouldBe(Blocks(one).Others);
    }

    [Theory]
    [InlineData("meses=0")]
    [InlineData("meses=13")]
    [InlineData("meses=-1")]
    [InlineData("meses=99999999999999999999")]
    [Trait("AC", "006-31")]
    public async Task Charts_MesesOutOfRange_Is400(string query)
    {
        var (status, body) = await _kit.SendAsync("/api/dashboard/charts?" + query, _user);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(DashboardTestKit.Error("Parâmetro 'meses' deve estar entre 1 e 12"));
    }

    [Theory]
    [InlineData("meses=abc", 2)]
    [InlineData("meses=", 2)]
    [InlineData("meses=2.5", 2)]
    [InlineData("meses=%205", 3)]
    [InlineData("meses=%2B1", 1)]
    [InlineData("meses=1&meses=12", 1)]
    [InlineData("meses=12&meses=1", 3)]
    [InlineData("meses=1", 1)]
    [Trait("AC", "006-31")]
    [Trait("AC", "006-32")]
    public async Task Charts_MesesParsing_FollowsPython(string query, int expectedMonthsWithData)
    {
        await SeedChartsAsync();
        // Padrão 3 ⇒ janela desde 2026-08-01 (set e out); 5 ou mais ⇒ desde jun ou antes (jul, set e out); 1 ⇒ só out.
        var (status, body) = await _kit.SendAsync("/api/dashboard/charts?" + query, _user);

        status.ShouldBe(HttpStatusCode.OK);
        Blocks(body).Monthly.Count.ShouldBe(expectedMonthsWithData);
    }

    [Fact]
    [Trait("AC", "006-35")]
    [Trait("AC", "006-73")]
    public async Task Charts_Categories_AreTheTenLargestFirst()
    {
        var subcategories = (await _kit.Bills.QueryAsync<int>("SELECT id FROM subcategoria ORDER BY id LIMIT 12")).ToList();
        for (var i = 0; i < subcategories.Count; i++)
        {
            await _kit.Transactions.InsertTransactionAsync(
                _user, _account, "Despesa", -(i + 1) * 10m, "2026-10-02", subcategoryId: subcategories[i]);
        }

        var (_, body) = await _kit.SendAsync("/api/dashboard/charts", _user);

        using var document = JsonDocument.Parse(body);
        var totals = document.RootElement.GetProperty("data").GetProperty("gastos_categoria").EnumerateArray()
            .Select(item => item.GetProperty("total").GetDouble()).ToList();
        // Desvio 092: maior gasto primeiro; as duas menores (10 e 20) ficam de fora do top 10.
        totals.ShouldBe([120, 110, 100, 90, 80, 70, 60, 50, 40, 30]);
    }

    [Fact]
    [Trait("AC", "006-36")]
    public async Task Charts_Weekday_WindowIsNinetyDays()
    {
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -10m, "2026-07-17");
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -99m, "2026-07-16");

        var (_, body) = await _kit.SendAsync("/api/dashboard/charts", _user);

        // 2026-07-17 é sexta-feira.
        body.ShouldContain("\"gastos_dia_semana\":[{\"dia\":\"Friday\",\"total\":10,\"quantidade\":1}]");
    }

    [Fact]
    [Trait("AC", "006-39")]
    public async Task Charts_WithoutExpenses_AreEmptyBlocks()
    {
        var (status, body) = await _kit.SendAsync("/api/dashboard/charts", _user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(DashboardTestKit.Ok("""{"gastos_mensais":[],"gastos_categoria":[],"gastos_dia_semana":[]}"""));
    }

    private static (List<string> Monthly, string Others) Blocks(string body)
    {
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        var monthly = data.GetProperty("gastos_mensais").EnumerateArray().Select(m => m.GetProperty("mes").GetString()!).ToList();
        return (monthly, data.GetProperty("gastos_categoria").GetRawText() + data.GetProperty("gastos_dia_semana").GetRawText());
    }

    // ============================== recentes ==============================

    [Fact]
    [Trait("AC", "006-43")]
    public async Task Recent_WithoutTransactions_IsEmptyList()
    {
        var (status, body) = await _kit.SendAsync("/api/dashboard/recent", _user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":[]}""");
    }

    [Fact]
    [Trait("AC", "006-40")]
    public async Task Recent_ReturnsTheTenNewest_ByDateThenCreatedAtThenId()
    {
        for (var day = 1; day <= 12; day++)
        {
            await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -day, $"2026-10-{day:D2}", $"t{day}");
        }

        // Mesma data do t12, mas criada depois e depois ainda (mesmo created_at: o id maior vem primeiro).
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -1m, "2026-10-12", "tarde", createdAt: "2099-01-01T00:00:00Z");

        var (_, body) = await _kit.SendAsync("/api/dashboard/recent", _user);

        using var document = JsonDocument.Parse(body);
        var names = document.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("descricao").GetString()).ToList();
        names.ShouldBe(["tarde", "t12", "t11", "t10", "t9", "t8", "t7", "t6", "t5", "t4"]);
    }

    [Fact]
    [Trait("AC", "006-41")]
    [Trait("AC", "006-42")]
    public async Task Recent_ReproducesTheFlaskBody_WithRawTypes()
    {
        var inactive = await _kit.Transactions.Accounts.InsertAccountAsync(_user, "Antiga", "Dinheiro", ativa: false);
        var income = await _kit.Transactions.InsertTransactionAsync(
            _user, _account, "Renda", 100m, "2026-10-05", "Salário", subcategoryId: TransactionsTestData.IncomeSubcategory);
        var expense = await _kit.Transactions.InsertTransactionAsync(
            _user, inactive, "Despesa", -85.9m, "2026-10-04", "Mercado", consolidada: false);
        var transfer = await _kit.Transactions.InsertTransactionAsync(_user, _account, "Transferência", -5m, "2026-10-03", "Transf");
        var payment = await _kit.Transactions.InsertTransactionAsync(_user, _account, "Pagamento Fatura", -9m, "2026-10-02", "Fatura");

        var (status, body) = await _kit.SendAsync("/api/dashboard/recent", _user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$"""{"status":"success","data":[{"id":{{income}},"descricao":"Salário","valor":100,"tipo":"Renda","data":"2026-10-05","categoria":"Receitas Gerais","conta":"Corrente"},""" +
            $$"""{"id":{{expense}},"descricao":"Mercado","valor":-85.9,"tipo":"Despesa","data":"2026-10-04","categoria":"Despesas Gerais","conta":"Antiga"},""" +
            $$"""{"id":{{transfer}},"descricao":"Transf","valor":-5,"tipo":"Transferência","data":"2026-10-03","categoria":"Despesas Gerais","conta":"Corrente"},""" +
            $$"""{"id":{{payment}},"descricao":"Fatura","valor":-9,"tipo":"Pagamento Fatura","data":"2026-10-02","categoria":"Despesas Gerais","conta":"Corrente"}]}""");
    }

    // ============================== alertas ==============================

    [Fact]
    [Trait("AC", "006-50")]
    [Trait("AC", "006-52")]
    [Trait("AC", "006-53")]
    [Trait("AC", "006-54")]
    [Trait("AC", "006-55")]
    [Trait("AC", "006-57")]
    [Trait("AC", "006-58")]
    [Trait("AC", "006-59")]
    public async Task Alerts_ReproducesTheFlaskBody()
    {
        var bills = _kit.Bills;
        var today = await bills.InsertScheduleAsync(_user, _account, "Hoje", "FIXO", "MENSAL", dia: 15, valor: 100m);
        var tomorrow = await bills.InsertScheduleAsync(_user, _account, "Amanhã", "FIXO", "MENSAL", dia: 16, valor: 0m);
        var seven = await bills.InsertScheduleAsync(_user, _account, "Em sete", "LEMBRETE_VARIAVEL", "MENSAL", dia: 22, valor: null);
        await bills.InsertScheduleAsync(_user, _account, "Fora", "FIXO", "MENSAL", dia: 23, valor: 5m);
        await bills.InsertScheduleAsync(_user, _account, "Inativo", "FIXO", "MENSAL", dia: 15, valor: 5m, ativo: false);
        var annual = await bills.InsertScheduleAsync(_user, _account, "Anual", "FIXO", "ANUAL", dia: 18, mes: 10, valor: 1200.5m);
        var weekly = await bills.InsertScheduleAsync(
            _user, _account, "Semanal", "FIXO", "SEMANAL", dia: 5, valor: 30m, dataInicio: "2026-10-05");
        var other = await bills.InsertUserAsync("5511777777777", "Outro");
        var otherAccount = await bills.InsertAccountAsync(other, "Outra");
        await bills.InsertScheduleAsync(other, otherAccount, "De outro", "FIXO", "MENSAL", dia: 15, valor: 5m);

        var (status, body) = await _kit.SendAsync("/api/dashboard/alerts", _user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(DashboardTestKit.Ok(
            $$"""[{"id":"{{today}}","descricao":"Hoje","subcategoria":"Outros","valor_previsto":100,"dia_execucao":15,"dias_restantes":0,"data_vencimento":"2026-10-15","tipo":"danger","tipo_agendamento":"FIXO"},""" +
            $$"""{"id":"{{tomorrow}}","descricao":"Amanhã","subcategoria":"Outros","valor_previsto":null,"dia_execucao":16,"dias_restantes":1,"data_vencimento":"2026-10-16","tipo":"warning","tipo_agendamento":"FIXO"},""" +
            $$"""{"id":"{{annual}}","descricao":"Anual","subcategoria":"Outros","valor_previsto":1200.5,"dia_execucao":18,"dias_restantes":3,"data_vencimento":"2026-10-18","tipo":"info","tipo_agendamento":"FIXO"},""" +
            $$"""{"id":"{{weekly}}","descricao":"Semanal","subcategoria":"Outros","valor_previsto":30,"dia_execucao":5,"dias_restantes":4,"data_vencimento":"2026-10-19","tipo":"info","tipo_agendamento":"FIXO"},""" +
            $$"""{"id":"{{seven}}","descricao":"Em sete","subcategoria":"Outros","valor_previsto":null,"dia_execucao":22,"dias_restantes":7,"data_vencimento":"2026-10-22","tipo":"info","tipo_agendamento":"LEMBRETE_VARIAVEL"}]"""));
    }

    [Fact]
    [Trait("AC", "006-50")]
    public async Task Alerts_OfAnInactiveAccount_StillAppear()
    {
        var inactive = await _kit.Bills.InsertAccountAsync(_user, "Antiga", ativa: false);
        await _kit.Bills.InsertScheduleAsync(_user, inactive, "Da antiga", dia: 15);

        var (_, body) = await _kit.SendAsync("/api/dashboard/alerts", _user);

        body.ShouldContain("Da antiga");
    }

    private async Task<List<string>> AlertNamesAsync()
    {
        var (status, body) = await _kit.SendAsync("/api/dashboard/alerts", _user);
        status.ShouldBe(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("descricao").GetString()!).ToList();
    }

    [Fact]
    [Trait("AC", "006-71")]
    public async Task Alerts_ScheduleThatHasNotStarted_DoesNotAppear()
    {
        // Desvio 093: parcela do bot que só começa no mês seguinte.
        await _kit.Bills.InsertScheduleAsync(_user, _account, "Ainda não começou", dia: 18, dataInicio: "2026-11-18");
        await _kit.Bills.InsertScheduleAsync(_user, _account, "Anual futura", periodicidade: "ANUAL", dia: 18, mes: 10, dataInicio: "2026-10-19");
        await _kit.Bills.InsertScheduleAsync(_user, _account, "Começa no dia", dia: 18, dataInicio: "2026-10-18");

        (await AlertNamesAsync()).ShouldBe(["Começa no dia"]);
    }

    [Fact]
    [Trait("AC", "006-72")]
    public async Task Alerts_BillAlreadyPaidInTheMonth_DoesNotAppear()
    {
        // Desvio 094: vínculo por agendamento_id; descrição só como fallback para transação antiga sem vínculo.
        var linked = await _kit.Bills.InsertScheduleAsync(_user, _account, "Paga com vínculo", dia: 18);
        await _kit.Bills.InsertScheduleAsync(_user, _account, "Paga sem vínculo", dia: 19);
        await _kit.Bills.InsertScheduleAsync(_user, _account, "Pendente", dia: 20);
        var otherMonth = await _kit.Bills.InsertScheduleAsync(_user, _account, "Paga mês passado", dia: 21);
        var paid = await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -10m, "2026-10-12", "qualquer nome");
        await _kit.Bills.ExecuteAsync("UPDATE transacoes SET agendamento_id = @id WHERE id = @paid", new { id = linked, paid });
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -10m, "2026-10-13", "Paga sem vínculo");
        var old = await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -10m, "2026-09-21", "x");
        await _kit.Bills.ExecuteAsync("UPDATE transacoes SET agendamento_id = @id WHERE id = @old", new { id = otherMonth, old });

        (await AlertNamesAsync()).ShouldBe(["Pendente", "Paga mês passado"]);
    }

    [Fact]
    [Trait("AC", "006-72")]
    public async Task Alerts_OtherUsersTransactionWithSameDescription_DoesNotHideTheBill()
    {
        await _kit.Bills.InsertScheduleAsync(_user, _account, "Luz", dia: 18);
        var other = await _kit.Transactions.InsertUserAsync("5511666666666", "Outro");
        var otherAccount = await _kit.Transactions.InsertAccountAsync(other, "Conta B");
        await _kit.Transactions.InsertTransactionAsync(other, otherAccount, "Despesa", -10m, "2026-10-12", "Luz");

        (await AlertNamesAsync()).ShouldBe(["Luz"]);
    }

    [Fact]
    [Trait("AC", "006-74")]
    public async Task Alerts_IncomeSchedules_DoNotAppear()
    {
        // Desvio 095: grupo Renda e macro Receitas Gerais saem; despesa segue.
        await _kit.Bills.InsertScheduleAsync(
            _user, _account, "Salário", dia: 15, subcategoryId: TransactionsTestData.IncomeSubcategory);
        await _kit.Bills.InsertScheduleAsync(_user, _account, "Aluguel", dia: 15);

        (await AlertNamesAsync()).ShouldBe(["Aluguel"]);
    }

    [Fact]
    [Trait("AC", "006-62")]
    public async Task Alerts_WithoutSchedules_AreEmpty()
    {
        var (status, body) = await _kit.SendAsync("/api/dashboard/alerts", _user);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","data":[]}""");
    }

    // ============================== isolamento ==============================

    [Fact]
    [Trait("AC", "006-06")]
    public async Task EachUser_OnlySeesTheirOwnData()
    {
        var other = await _kit.Transactions.InsertUserAsync("5511666666666", "Outro");
        var otherAccount = await _kit.Transactions.InsertAccountAsync(other, "Conta B");
        await _kit.Transactions.InsertTransactionAsync(_user, _account, "Despesa", -11m, "2026-10-05", "da A");
        await _kit.Transactions.InsertTransactionAsync(other, otherAccount, "Despesa", -22m, "2026-10-05", "da B");
        await _kit.Bills.InsertScheduleAsync(other, otherAccount, "Alerta B", dia: 15);

        var (_, recentA) = await _kit.SendAsync("/api/dashboard/recent", _user);
        var (_, recentB) = await _kit.SendAsync("/api/dashboard/recent", other);
        var (_, summaryA) = await _kit.SendAsync("/api/dashboard/summary", _user);
        var (_, chartsB) = await _kit.SendAsync("/api/dashboard/charts", other);
        var (_, alertsA) = await _kit.SendAsync("/api/dashboard/alerts", _user);

        recentA.ShouldContain("da A");
        recentA.ShouldNotContain("da B");
        recentB.ShouldContain("da B");
        recentB.ShouldNotContain("da A");
        summaryA.ShouldContain("\"despesas_mes\":11,");
        chartsB.ShouldContain("\"total\":22");
        alertsA.ShouldNotContain("Alerta B");
    }

    // ============================== erros ==============================

    [Theory]
    [InlineData("/api/dashboard/summary", "Erro ao carregar resumo do dashboard")]
    [InlineData("/api/dashboard/charts", "Erro ao carregar dados dos gráficos")]
    [InlineData("/api/dashboard/recent", "Erro ao carregar transações recentes")]
    [InlineData("/api/dashboard/alerts", "Erro ao buscar alertas")]
    [Trait("AC", "006-05")]
    public async Task RepositoryFailure_Returns500WithTheFixedMessage_AndLogsOnlyTheErrorType(string path, string message)
    {
        var log = new CapturingLogger();
        await using var factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddScoped<IDashboardRepository>(_ => new ThrowingRepository());
            services.AddSingleton<ILogger<DashboardModule>>(log);
        }));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + AccountsHttp.TokenFor(42));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(DashboardTestKit.Error(message));
        body.ShouldNotContain("segredo");
        log.Messages.Count.ShouldBe(1);
        log.Messages[0].ShouldContain("PostgresException (SqlState 23505)");
        log.Messages[0].ShouldContain("usuário 42");
        log.Messages[0].ShouldNotContain("segredo");
    }

    private sealed class ThrowingRepository : IDashboardRepository
    {
        private static PostgresException Failure() => new("segredo interno", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);

        public Task<IReadOnlyList<decimal>> ListActiveAccountBalancesAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<TypeTotal>> SumByTypeAsync(int userId, DateOnly firstDay, DateOnly lastDay, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<MonthlyExpense>> MonthlyExpensesAsync(int userId, DateOnly from, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<CategoryExpense>> CategoryExpensesAsync(int userId, DateOnly from, DateOnly toExclusive, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<WeekdayExpense>> WeekdayExpensesAsync(int userId, DateOnly from, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<RecentTransaction>> ListRecentAsync(int userId, CancellationToken cancellationToken) => throw Failure();

        public Task<IReadOnlyList<ScheduleRow>> ListActiveSchedulesAsync(int userId, DateOnly paidFrom, DateOnly paidToExclusive, CancellationToken cancellationToken) => throw Failure();
    }

    private sealed class CapturingLogger : ILogger<DashboardModule>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages)
            {
                Messages.Add(formatter(state, exception));
            }
        }
    }
}
