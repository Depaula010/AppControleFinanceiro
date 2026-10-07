using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Automation;

/// <summary><c>POST /webhook-sms-payment</c> (spec 011 §3.3).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class SmsPaymentEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AutomationEnv _env = default!;
    private int _user;
    private int _wallet;
    private int _checking;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _user = await _env.SeedUserAsync();
        _wallet = await _env.Data.InsertAccountAsync(_user, "Carteira");
        _checking = await _env.Data.InsertAccountAsync(_user, "Nubank");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Body(string description = "conta de luz", string value = "150.5", string account = "swile", string? date = null) =>
        "{" + $"\"user_api_key\":\"{AutomationEnv.UserKey}\",\"descricao_pagamento\":\"{description}\",\"valor_pago\":{value},\"conta_pagamento\":\"{account}\""
        + (date is null ? string.Empty : $",\"data_pagamento\":\"{date}\"") + "}";

    [Fact]
    [Trait("AC", "011-061")]
    public async Task MatchingBill_IsSettledOnTheScheduleAccount()
    {
        var schedule = await _env.InsertScheduleAsync(_user, _checking, "Luz", subcategoryId: 59);

        var (status, body) = await _env.Post("/webhook-sms-payment", Body(date: "2026-10-02"));

        var row = (await _env.QueryAsync<(int Id, decimal Valor, string Tipo, string Descricao, int Conta, int Sub, int? Schedule, DateTime Data, int? Fatura, string? Origem)>(
            "SELECT id, valor, tipo_transacao, descricao, conta_id, subcategoria_id, agendamento_id, data_transacao, fatura_id, origem FROM transacoes")).Single();
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe($$"""{"status":"sucesso","mensagem":"Conta fixa quitada com sucesso!","conta_identificada":"Luz","transaction_id":{{row.Id}}}""");
        (row.Valor, row.Tipo, row.Descricao, row.Conta, row.Sub, row.Schedule, DateOnly.FromDateTime(row.Data), row.Fatura, row.Origem)
            .ShouldBe((-150.50m, "Despesa", "Luz", _checking, 59, null, new DateOnly(2026, 10, 2), null, null));
        _ = schedule;
        _env.Bot.Sent.Single().Message.ShouldBe(
            "✅ *CONTA QUITADA!* ✅\n\n📝 Luz\n💰 R$ 150,50\n📊 Outros\n\n_Esta conta não será mais cobrada automaticamente este mês._");
        _env.Llm.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "011-062")]
    public async Task ABillOnACard_IsLinkedToTheInvoiceOfThePaymentDate()
    {
        var card = await _env.Data.InsertCardAsync(_user, "Cartão Inter", 10, 15);
        await _env.InsertScheduleAsync(_user, card, "Netflix", subcategoryId: 59);

        var (status, _) = await _env.Post("/webhook-sms-payment", Body("netflix", "39.9", date: "2026-10-05"));

        status.ShouldBe(HttpStatusCode.OK);
        var invoice = (await _env.QueryAsync<(int Id, DateTime Due)>("SELECT id, data_vencimento FROM faturas")).Single();
        DateOnly.FromDateTime(invoice.Due).ShouldBe(new DateOnly(2026, 10, 15));
        (await _env.ScalarAsync<int?>("SELECT fatura_id FROM transacoes")).ShouldBe(invoice.Id);
    }

    [Fact]
    [Trait("AC", "011-061")]
    public async Task InstallmentsAndInactiveSchedules_NeverMatch()
    {
        await _env.InsertScheduleAsync(_user, _checking, "Luz", type: "PARCELADO", total: 3, executed: 1);
        await _env.InsertScheduleAsync(_user, _checking, "Luz", active: false);
        _env.Llm.Then("{}").Then("59");

        var (status, body) = await _env.Post("/webhook-sms-payment", Body());

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("Despesa registrada");
    }

    [Fact]
    [Trait("AC", "011-063")]
    public async Task NoMatch_RegistersALooseExpenseOnTheWallet()
    {
        _env.Llm.Then("""{"valor_decimal": 80.5, "descricao_bruta": "Mercado"}""").Then("59");

        var (status, body) = await _env.Post("/webhook-sms-payment", Body("Mercado Extra", "80.5", "swile", "2026-10-03"));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","mensagem":"Despesa registrada","conta_identificada":null}""");
        var row = (await _env.QueryAsync<(decimal Valor, string Descricao, int Conta, int Sub, int? Fatura, DateTime Data)>(
            "SELECT valor, descricao, conta_id, subcategoria_id, fatura_id, data_transacao FROM transacoes")).Single();
        (row.Valor, row.Descricao, row.Conta, row.Sub, row.Fatura, DateOnly.FromDateTime(row.Data))
            .ShouldBe((-80.50m, "Mercado Extra (swile)", _wallet, 59, null, new DateOnly(2026, 10, 3)));
        _env.Bot.Sent.Single().Message.ShouldBe(
            "Pagamento Registrado!\n\nMercado Extra\nR$ 80,50\nswile\n\n_Nao encontrei uma conta fixa correspondente, entao registrei como despesa avulsa._");
        _env.Llm.Requests[0].Prompt.ShouldStartWith("Analise a mensagem: \"Paguei 80.5 de Mercado Extra\"\n");
    }

    [Fact]
    [Trait("AC", "011-063")]
    public async Task NoMatch_WithoutWalletUsesTheFirstActiveAccount()
    {
        await _env.ScalarAsync<int>("UPDATE contas SET ativa = false WHERE id = @_wallet RETURNING id", new { _wallet });
        _env.Llm.Then("{}").Then("59");

        await _env.Post("/webhook-sms-payment", Body("Mercado Extra"));

        (await _env.ScalarAsync<int>("SELECT conta_id FROM transacoes")).ShouldBe(_checking);
    }

    [Fact]
    [Trait("AC", "011-063")]
    public async Task TheUselessGeminiCallFailingTurnsIntoA500_WithNothingSaved()
    {
        _env.Llm.Then("isto não é json");

        var (status, body) = await _env.Post("/webhook-sms-payment", Body("Mercado Extra"));

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"Erro ao processar o pagamento. Tente novamente."}""");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "011-065")]
    public async Task TextAmount_SavesAndThenAnswers500WithoutNotifying()
    {
        await _env.InsertScheduleAsync(_user, _checking, "Luz");

        var (status, body) = await _env.Post("/webhook-sms-payment", Body(value: "\"150.50\""));

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"Erro ao processar o pagamento. Tente novamente."}""");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(1);
        _env.Bot.Sent.ShouldBeEmpty();
    }

    [Theory]
    [Trait("AC", "011-066")]
    [InlineData("2026-02-30")]
    [InlineData("15/01/2026")]
    public async Task InvalidDate_Is500_WithNothingSaved(string date)
    {
        await _env.InsertScheduleAsync(_user, _checking, "Luz");

        var (status, _) = await _env.Post("/webhook-sms-payment", Body(date: date));

        status.ShouldBe(HttpStatusCode.InternalServerError);
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "011-067")]
    public async Task NegativeAmount_Is500_WithNothingSaved()
    {
        await _env.InsertScheduleAsync(_user, _checking, "Luz");

        var (status, _) = await _env.Post("/webhook-sms-payment", Body(value: "-10"));

        status.ShouldBe(HttpStatusCode.InternalServerError);
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "011-060")]
    public async Task MissingData_AndUnknownKey()
    {
        (await _env.Post("/webhook-sms-payment", """{"user_api_key":"x","descricao_pagamento":"luz","valor_pago":0,"conta_pagamento":"swile"}"""))
            .ShouldBe((HttpStatusCode.BadRequest, """{"status":"erro","mensagem":"Dados faltando"}"""));
        (await _env.Post("/webhook-sms-payment", Body().Replace(AutomationEnv.UserKey, "outra", StringComparison.Ordinal)))
            .ShouldBe((HttpStatusCode.Unauthorized, """{"status":"erro","mensagem":"API key invalida"}"""));
        (await _env.Post("/webhook-sms-payment", null)).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    [Trait("AC", "011-060")]
    public async Task DatabaseDown_Is503WithTheRespostaKey()
    {
        var env = new AutomationEnv(postgres);
        var broken = env.Factory.WithWebHostBuilder(builder => builder.UseSetting("DATABASE_URL", "postgresql://u:s@127.0.0.1:1/x"));
        using var client = broken.CreateClient();

        using var response = await client.PostAsync(new Uri("/webhook-sms-payment", UriKind.Relative), new StringContent(Body(), System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"status":"erro","resposta":"Banco de dados temporariamente indisponivel"}""");
    }

    [Fact]
    [Trait("AC", "011-005")]
    public async Task UnexpectedError_NeverLeaksTheKey()
    {
        await _env.InsertScheduleAsync(_user, _checking, "Luz");

        var (status, body) = await _env.Post("/webhook-sms-payment", Body(value: "\"abc\""));

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldNotContain(AutomationEnv.UserKey);
        _env.Logs.All.ShouldNotContain(AutomationEnv.UserKey);
        _env.Logs.Entries.ShouldContain(e => e.Message.Contains("AutomationFailureException"));
    }
}

/// <summary><c>PATCH /api/agendamento/{id}/reserva</c> e <c>GET /api/agendamentos/reserva</c> (spec 011 §3.4 e §3.5).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class ReserveEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AutomationEnv _env = default!;
    private int _user;
    private int _checking;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _user = await _env.SeedUserAsync();
        _checking = await _env.Data.InsertAccountAsync(_user, "Nubank");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<(HttpStatusCode Status, string Body)> Patch(int id, string? json) => _env.SendAsync(HttpMethod.Patch, $"/api/agendamento/{id}/reserva", json);

    private Task<(HttpStatusCode Status, string Body)> Get(string query) => _env.SendAsync(HttpMethod.Get, "/api/agendamentos/reserva" + query);

    private static string Toggle(bool include, string key = AutomationEnv.UserKey) => $$"""{"incluir":{{(include ? "true" : "false")}},"api_key":"{{key}}"}""";

    [Fact]
    [Trait("AC", "011-081")]
    public async Task Toggle_UpdatesTheFlag_AndReportsTheImpact()
    {
        var netflix = await _env.InsertScheduleAsync(_user, _checking, "Netflix", amount: 49.90m, includeInReserve: false);
        await _env.InsertScheduleAsync(_user, _checking, "Aluguel", amount: 1500.00m, includeInReserve: true);

        var (status, body) = await Patch(netflix, Toggle(true));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            $$$"""{"status":"sucesso","mensagem":"Agendamento 'Netflix' incluído do cálculo de reserva","agendamento":{"id":{{{netflix}}},"descricao":"Netflix","valor_previsto":49.9,"periodicidade":"MENSAL","tipo_agendamento":"FIXO","incluir_na_reserva":true},"impacto":{"gasto_mensal_anterior":1500,"gasto_mensal_novo":1549.9000000000003,"reserva_ideal_anterior":9000,"reserva_ideal_nova":9299.400000000001}}""");
        (await _env.ScalarAsync<bool>("SELECT incluir_na_reserva FROM agendamentos WHERE id = @netflix", new { netflix })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "011-081")]
    public async Task Toggle_False_WritesFalseAndUsesTheExcludedText()
    {
        var netflix = await _env.InsertScheduleAsync(_user, _checking, "Netflix", amount: 49.90m);

        var (status, body) = await Patch(netflix, Toggle(false));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("excluído do cálculo de reserva");
        body.ShouldContain("\"incluir_na_reserva\":false");
        (await _env.ScalarAsync<bool>("SELECT incluir_na_reserva FROM agendamentos WHERE id = @netflix", new { netflix })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "011-083")]
    public async Task Toggle_OnAnInstallment_ChangesTheColumnButNotTheReserve()
    {
        var installment = await _env.InsertScheduleAsync(_user, _checking, "Notebook", type: "PARCELADO", total: 3, executed: 1, amount: 300.00m, includeInReserve: false);

        var (_, body) = await Patch(installment, Toggle(true));

        body.ShouldContain("\"impacto\":{\"gasto_mensal_anterior\":0,\"gasto_mensal_novo\":0,\"reserva_ideal_anterior\":0,\"reserva_ideal_nova\":0}");
        (await _env.ScalarAsync<bool>("SELECT incluir_na_reserva FROM agendamentos WHERE id = @installment", new { installment })).ShouldBeTrue();
    }

    [Theory]
    [Trait("AC", "011-080")]
    [InlineData("{}")]
    [InlineData("""{"incluir":null,"api_key":"x"}""")]
    [InlineData("""{"incluir":true}""")]
    public async Task Toggle_MissingFields_Is400(string json)
    {
        var (status, body) = await Patch(1, json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"status":"erro","mensagem":"Campos 'incluir' e 'api_key' são obrigatórios"}""");
    }

    [Fact]
    [Trait("AC", "011-080")]
    public async Task Toggle_UnknownKey_Is401WithAccent()
    {
        var (status, body) = await Patch(1, Toggle(true, "outra"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe("""{"status":"erro","mensagem":"API key inválida"}""");
    }

    [Fact]
    [Trait("AC", "011-082")]
    public async Task Toggle_UnknownInactiveOrForeignSchedule_Is404()
    {
        var other = await _env.SeedUserAsync("5511999990001", "outra-chave");
        var otherAccount = await _env.Data.InsertAccountAsync(other, "Conta");
        var foreign = await _env.InsertScheduleAsync(other, otherAccount, "Alheio");
        var inactive = await _env.InsertScheduleAsync(_user, _checking, "Inativo", active: false);

        foreach (var id in new[] { 999, foreign, inactive })
        {
            var (status, body) = await Patch(id, Toggle(true));
            status.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe("""{"status":"erro","mensagem":"Agendamento não encontrado ou inativo"}""");
        }

        (await _env.ScalarAsync<bool>("SELECT incluir_na_reserva FROM agendamentos WHERE id = @foreign", new { foreign })).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "011-084")]
    public async Task Toggle_NonBoolean_Is500_AndNothingChanges()
    {
        var netflix = await _env.InsertScheduleAsync(_user, _checking, "Netflix", includeInReserve: false);

        var (status, body) = await Patch(netflix, """{"incluir":"true","api_key":"chave-do-atalho-0001"}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"Erro ao atualizar a reserva. Tente novamente."}""");
        (await _env.ScalarAsync<bool>("SELECT incluir_na_reserva FROM agendamentos WHERE id = @netflix", new { netflix })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "011-085")]
    public async Task Toggle_HugeId_Is404()
    {
        var (status, body) = await _env.SendAsync(HttpMethod.Patch, "/api/agendamento/99999999999999999999/reserva", Toggle(true));

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe("""{"status":"erro","mensagem":"Agendamento não encontrado ou inativo"}""");
    }

    [Fact]
    [Trait("AC", "011-004")]
    public async Task Routing_NonNumericId_Is404_AndOtherMethodsAre405()
    {
        (await _env.SendAsync(HttpMethod.Patch, "/api/agendamento/abc/reserva", Toggle(true))).Status.ShouldBe(HttpStatusCode.NotFound);
        (await _env.SendAsync(HttpMethod.Patch, "/api/agendamento/-1/reserva", Toggle(true))).Status.ShouldBe(HttpStatusCode.NotFound);
        (await _env.SendAsync(HttpMethod.Patch, "/api/agendamento/1/reserva/", Toggle(true))).Status.ShouldBe(HttpStatusCode.NotFound);
        (await _env.SendAsync(HttpMethod.Get, "/api/agendamento/1/reserva")).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await _env.SendAsync(HttpMethod.Post, "/api/agendamentos/reserva", "{}")).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await Get("/?api_key=x")).Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("AC", "011-101")]
    public async Task List_ReturnsTheActiveSchedulesOrderedWithTheReserveSummary()
    {
        await _env.InsertScheduleAsync(_user, _checking, "Netflix", amount: 49.90m, day: 20);
        await _env.InsertScheduleAsync(_user, _checking, "Aluguel", amount: 1500.00m, day: 5, subcategoryId: 59);
        await _env.InsertScheduleAsync(_user, _checking, "Inativo", amount: 9999.00m, active: false);

        var (status, body) = await Get($"?api_key={AutomationEnv.UserKey}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","total":2,"limit":100,"offset":0,"agendamentos":[{"id":2,"descricao":"Aluguel","valor_previsto":1500,"periodicidade":"MENSAL","tipo_agendamento":"FIXO","dia_execucao":5,"categoria":"Outros","macro_categoria":"Despesas Gerais","grupo":"Geral","incluir_na_reserva":true},{"id":1,"descricao":"Netflix","valor_previsto":49.9,"periodicidade":"MENSAL","tipo_agendamento":"FIXO","dia_execucao":20,"categoria":"Outros","macro_categoria":"Despesas Gerais","grupo":"Geral","incluir_na_reserva":true}],"resumo_reserva":{"gasto_mensal_essencial":1549.9000000000003,"reserva_ideal":9299.400000000001,"meses_configurados":6}}""");
    }

    [Fact]
    [Trait("AC", "011-102")]
    public async Task List_Filters_ByFlagPeriodicityAndCategory_AndPaginates()
    {
        await _env.InsertScheduleAsync(_user, _checking, "A", amount: 30.00m, includeInReserve: true);
        await _env.InsertScheduleAsync(_user, _checking, "B", amount: 20.00m, includeInReserve: false);
        await _env.InsertScheduleAsync(_user, _checking, "C", amount: 10.00m, periodicity: "ANUAL", month: 3, includeInReserve: true);

        Task<JsonElement> Run(string query) => Json(Get($"?api_key={AutomationEnv.UserKey}{query}"));

        (await Run("&incluir_na_reserva=TRUE")).GetProperty("agendamentos").EnumerateArray().Select(a => a.GetProperty("descricao").GetString()).ShouldBe(["A", "C"]);
        (await Run("&incluir_na_reserva=abc")).GetProperty("agendamentos").EnumerateArray().Select(a => a.GetProperty("descricao").GetString()).ShouldBe(["B"]);
        (await Run("&periodicidade=anual")).GetProperty("total").GetInt32().ShouldBe(1);
        (await Run("&categoria=OUTR")).GetProperty("total").GetInt32().ShouldBe(3);
        (await Run("&categoria=zzz")).GetProperty("total").GetInt32().ShouldBe(0);
        var page = await Run("&limit=1&offset=1");
        page.GetProperty("total").GetInt32().ShouldBe(3);
        page.GetProperty("agendamentos").EnumerateArray().Select(a => a.GetProperty("descricao").GetString()).ShouldBe(["B"]);
        (await Run("&limit=0")).GetProperty("agendamentos").GetArrayLength().ShouldBe(0);
        (await Run("&limit=%205%20")).GetProperty("limit").GetInt32().ShouldBe(5);
    }

    private static async Task<JsonElement> Json(Task<(HttpStatusCode Status, string Body)> call)
    {
        var (status, body) = await call;
        status.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [Theory]
    [Trait("AC", "011-100")]
    [InlineData("")]
    [InlineData("?api_key=")]
    public async Task List_MissingKey_Is400(string query)
    {
        var (status, body) = await Get(query);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"status":"erro","mensagem":"Parâmetro 'api_key' é obrigatório"}""");
    }

    [Fact]
    [Trait("AC", "011-100")]
    public async Task List_UnknownKey_Is401WithAccent()
    {
        var (status, body) = await Get("?api_key=outra");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe("""{"status":"erro","mensagem":"API key inválida"}""");
    }

    [Fact]
    [Trait("AC", "011-103")]
    public async Task List_InvalidPeriodicity_Is400()
    {
        var (status, body) = await Get($"?api_key={AutomationEnv.UserKey}&periodicidade=semestral");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"status":"erro","mensagem":"Periodicidade inválida. Use: DIARIA, SEMANAL, QUINZENAL, MENSAL ou ANUAL"}""");
    }

    [Theory]
    [Trait("AC", "011-105")]
    [InlineData("limit=abc")]
    [InlineData("limit=1.5")]
    [InlineData("limit=-1")]
    [InlineData("offset=-1")]
    [InlineData("offset=")]
    public async Task List_InvalidPaging_Is500(string query)
    {
        var (status, body) = await Get($"?api_key={AutomationEnv.UserKey}&{query}");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"Erro ao listar a reserva. Tente novamente."}""");
    }

    [Fact]
    [Trait("AC", "011-106")]
    public async Task List_WithoutSchedules_ReturnsAnEmptySummary()
    {
        var (status, body) = await Get($"?api_key={AutomationEnv.UserKey}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","total":0,"limit":100,"offset":0,"agendamentos":[],"resumo_reserva":{"gasto_mensal_essencial":0,"reserva_ideal":0,"meses_configurados":6}}""");
    }

    [Fact]
    [Trait("AC", "011-107")]
    public async Task List_AScheduleWithoutAmount_ShowsZeroAndSortsFirstLikePostgres()
    {
        await _env.InsertScheduleAsync(_user, _checking, "Com valor", amount: 10.00m);
        await _env.InsertScheduleAsync(_user, _checking, "Sem valor", amount: null);

        var json = await Json(Get($"?api_key={AutomationEnv.UserKey}"));

        var first = json.GetProperty("agendamentos")[0];
        first.GetProperty("descricao").GetString().ShouldBe("Sem valor");
        first.GetProperty("valor_previsto").GetDouble().ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "011-101")]
    public async Task List_NeverShowsAnotherUsersSchedules()
    {
        var other = await _env.SeedUserAsync("5511999990001", "outra-chave");
        var otherAccount = await _env.Data.InsertAccountAsync(other, "Conta");
        await _env.InsertScheduleAsync(other, otherAccount, "Alheio");

        var json = await Json(Get($"?api_key={AutomationEnv.UserKey}"));

        json.GetProperty("total").GetInt32().ShouldBe(0);
    }
}

/// <summary><c>POST /api/fatura/{id}/reprocessar</c> e o populador real (spec 011 §3.6, RN-011-14).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class ReprocessEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private AutomationEnv _env = default!;
    private int _user;
    private int _card;
    private int _invoice;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new AutomationEnv(postgres);
        _user = await _env.SeedUserAsync();
        _card = await _env.Data.InsertCardAsync(_user, "Cartão Inter", 10, 15);
        _invoice = await _env.Data.InsertInvoiceAsync(_card, "2026-04-15", "2026-04-10");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<(HttpStatusCode Status, string Body)> Reprocess(int id, string? key = "valid") =>
        _env.SendAsync(HttpMethod.Post, $"/api/fatura/{id}/reprocessar", apiKey: key == "valid" ? AutomationEnv.ApiKey : key);

    [Fact]
    [Trait("AC", "011-120")]
    public async Task WithoutTheApiKey_Is401_AndNothingIsTouched()
    {
        await _env.InsertScheduleAsync(_user, _card, "Netflix");

        foreach (var key in new string?[] { null, "errada" })
        {
            var (status, body) = await Reprocess(_invoice, key);
            status.ShouldBe(HttpStatusCode.Unauthorized);
            body.ShouldBe("""{"status":"erro","mensagem":"Não autorizado"}""");
        }

        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "011-121")]
    public async Task UnknownInvoice_Is404()
    {
        var (status, body) = await Reprocess(9999);

        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe("""{"status":"erro","mensagem":"Fatura ID 9999 não encontrada"}""");
    }

    [Fact]
    [Trait("AC", "011-122")]
    public async Task PostsTheSchedulesOfTheCard_WithTheExactStats()
    {
        var netflix = await _env.InsertScheduleAsync(_user, _card, "Netflix", amount: 49.90m, day: 31);
        var notebook = await _env.InsertScheduleAsync(_user, _card, "Notebook", type: "PARCELADO", total: 3, executed: 2, amount: 300.00m, day: 5);
        var salary = await _env.InsertScheduleAsync(_user, _card, "Bônus", type: "LEMBRETE_VARIAVEL", amount: 100.00m, day: 20, subcategoryId: TransactionsTestData.IncomeSubcategory);
        await _env.ScalarAsync<int>(
            "UPDATE agendamentos SET subcategoria_id = (SELECT s.id FROM subcategoria s JOIN macrocategoria m ON s.macro_id = m.id JOIN grupocategoria g ON m.grupo_id = g.id WHERE g.nome_grupo = 'Renda' ORDER BY s.id LIMIT 1) WHERE id = @salary RETURNING id",
            new { salary });

        var (status, body) = await Reprocess(_invoice);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","fatura_id":1,"cartao":"Cartão Inter","vencimento":"15/04/2026","status_fatura":"Aberta","stats":{"total_processados":3,"fixos":1,"parcelados":1,"lembretes":1,"detalhes":[{"tipo":"PARCELADO","descricao":"Notebook (3/3)","valor":"300.00"},{"tipo":"LEMBRETE_VARIAVEL","descricao":"Bônus","valor":"100.00"},{"tipo":"FIXO","descricao":"Netflix","valor":"49.90"}]}}""");

        var rows = await _env.QueryAsync<(string Descricao, decimal Valor, string Tipo, DateTime Data, int? Schedule, int? Fatura, bool Consolidada)>(
            "SELECT descricao, valor, tipo_transacao, data_transacao, agendamento_id, fatura_id, consolidada FROM transacoes ORDER BY id");
        rows.Count.ShouldBe(3);
        rows.Select(r => (r.Descricao, r.Valor, r.Tipo, DateOnly.FromDateTime(r.Data), r.Schedule, r.Fatura, r.Consolidada)).ShouldBe(
        [
            ("Notebook (3/3)", -300.00m, "Despesa", new DateOnly(2026, 4, 5), notebook, _invoice, true),
            ("Bônus", 100.00m, "Renda", new DateOnly(2026, 4, 20), salary, _invoice, true),
            ("Netflix", -49.90m, "Despesa", new DateOnly(2026, 4, 30), netflix, _invoice, true),
        ]);
        var installment = (await _env.QueryAsync<(int Executed, bool Active)>("SELECT parcelas_executadas, ativo FROM agendamentos WHERE id = @notebook", new { notebook })).Single();
        installment.ShouldBe((3, false));
    }

    [Fact]
    [Trait("AC", "011-123")]
    public async Task SecondCall_IsIdempotent()
    {
        await _env.InsertScheduleAsync(_user, _card, "Netflix", amount: 49.90m);
        await _env.InsertScheduleAsync(_user, _card, "Notebook", type: "PARCELADO", total: 4, executed: 1, amount: 300.00m);
        await Reprocess(_invoice);

        var (status, body) = await Reprocess(_invoice);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"stats\":{\"total_processados\":0,\"fixos\":0,\"parcelados\":0,\"lembretes\":0,\"detalhes\":[]}");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(2);
        (await _env.ScalarAsync<int>("SELECT parcelas_executadas FROM agendamentos WHERE tipo_agendamento = 'PARCELADO'")).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "011-124")]
    public async Task OnlyActiveSchedulesOfTheCard_AndAnnualOnlyInTheDueMonth()
    {
        var otherAccount = await _env.Data.InsertAccountAsync(_user, "Corrente");
        await _env.InsertScheduleAsync(_user, otherAccount, "Outra conta");
        await _env.InsertScheduleAsync(_user, _card, "Inativo", active: false);
        await _env.InsertScheduleAsync(_user, _card, "IPTU", periodicity: "ANUAL", month: 4, amount: 800.00m);
        await _env.InsertScheduleAsync(_user, _card, "Seguro", periodicity: "ANUAL", month: 9, amount: 900.00m);

        var (_, body) = await Reprocess(_invoice);

        body.ShouldContain("\"total_processados\":1");
        (await _env.ScalarAsync<string>("SELECT descricao FROM transacoes")).ShouldBe("IPTU");
    }

    [Fact]
    [Trait("AC", "011-125")]
    public async Task AScheduleWithoutAmount_PostsZero_AndReportsTheNumberZero()
    {
        await _env.InsertScheduleAsync(_user, _card, "Variável", type: "LEMBRETE_VARIAVEL", amount: null);

        var (_, body) = await Reprocess(_invoice);

        body.ShouldContain("\"detalhes\":[{\"tipo\":\"LEMBRETE_VARIAVEL\",\"descricao\":\"Variável\",\"valor\":0}]");
        (await _env.ScalarAsync<decimal>("SELECT valor FROM transacoes")).ShouldBe(0m);
    }

    [Fact]
    [Trait("AC", "011-004")]
    public async Task Routing()
    {
        (await _env.SendAsync(HttpMethod.Post, "/api/fatura/abc/reprocessar", apiKey: AutomationEnv.ApiKey)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await _env.SendAsync(HttpMethod.Post, "/api/fatura/1/reprocessar/", apiKey: AutomationEnv.ApiKey)).Status.ShouldBe(HttpStatusCode.NotFound);
        (await _env.SendAsync(HttpMethod.Get, "/api/fatura/1/reprocessar", apiKey: AutomationEnv.ApiKey)).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    [Trait("AC", "011-127")]
    public async Task ThePopulatorOfTheTransactionsModule_IsTheRealOne_AndFillsANewInvoice()
    {
        await _env.ScalarAsync<int>("WITH removed AS (DELETE FROM faturas RETURNING 1) SELECT count(*)::int FROM removed");
        await _env.InsertScheduleAsync(_user, _card, "Netflix", amount: 49.90m, day: 12);
        using var scope = _env.Factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IInvoiceSchedulePopulator>().ShouldBeOfType<InvoiceSchedulePopulator>();
        await scope.ServiceProvider.GetRequiredService<IInvoiceService>().EnsureCurrentAsync(_user, _card, CancellationToken.None);

        var invoice = (await _env.QueryAsync<(int Id, DateTime Due)>("SELECT id, data_vencimento FROM faturas")).Single();
        var posted = (await _env.QueryAsync<(string Descricao, int? Fatura, DateTime Data)>("SELECT descricao, fatura_id, data_transacao FROM transacoes")).Single();
        posted.Descricao.ShouldBe("Netflix");
        posted.Fatura.ShouldBe(invoice.Id);
        DateOnly.FromDateTime(posted.Data).ShouldBe(new DateOnly(invoice.Due.Year, invoice.Due.Month, 12));
    }
}
