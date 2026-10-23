using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using MeuSecretario.TestFixtures;

namespace MeuSecretario.IntegrationTests.Automation;

/// <summary><c>POST /webhook-automate</c> (spec 011 §3.1): banco real, Gemini, bot e Redis falsos; corpo como texto exato.</summary>
[Trait("Category", "Docker")]
public class AutomateEndpointTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Extraction = """{"valor_decimal": 45.9, "descricao_bruta": "Mercado", "tipo_fluxo": "Despesa"}""";
    private const string Sentinel = "SENTINELA-gemini-7741";

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

    private static string Body(string text = "Debito aprovado R$ 45,90", string key = AutomationEnv.UserKey) =>
        AutomationEnv.Json(new { texto = text, user_api_key = key });

    [Fact]
    [Trait("AC", "011-011")]
    public async Task Expense_StoresThePendingInRedisAndAsksForConfirmation_WithoutSaving()
    {
        _env.Llm.Then(Extraction).Then("59");

        var (status, body) = await _env.Post("/webhook-automate", Body());

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","mensagem":"Aguardando confirmacao do usuario","transaction_id":"ab12cd34"}""");

        var pending = JsonDocument.Parse(_env.Cache.Values[$"pending_tx:{AutomationEnv.Whatsapp}:ab12cd34"]).RootElement;
        pending.GetProperty("usuario_id").GetInt32().ShouldBe(_user);
        pending.GetProperty("conta_id").GetInt32().ShouldBe(_checking);
        pending.GetProperty("categoria_id").GetInt32().ShouldBe(59);
        pending.GetProperty("fatura_id").ValueKind.ShouldBe(JsonValueKind.Null);
        pending.GetProperty("descricao").GetString().ShouldBe("Mercado");
        pending.GetProperty("valor_db").GetDouble().ShouldBe(-45.9);
        pending.GetProperty("valor_original").GetDouble().ShouldBe(45.9);
        pending.GetProperty("tipo_transacao").GetString().ShouldBe("Despesa");
        pending.GetProperty("data_transacao").GetString().ShouldBe(_env.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        pending.GetProperty("origem").GetString().ShouldBe("automate");
        _env.Cache.Values[$"last_pending:{AutomationEnv.Whatsapp}"].ShouldBe("\"ab12cd34\"");

        var (number, message) = _env.Bot.Sent.Single();
        number.ShouldBe(AutomationEnv.Whatsapp);
        message.ShouldContain("📍 *Mercado*");
        message.ShouldContain("📂 Despesas Gerais → Outros");
        message.ShouldContain("_ID: ab12cd34 | Expira em 5 min_");

        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
        _env.Llm.Requests.Count.ShouldBe(2);
        _env.Llm.Requests[1].UserId.ShouldBe(_user);
    }

    [Fact]
    [Trait("AC", "011-012")]
    public async Task CardWords_UseTheCard_AndTheInvoiceOfTodayIsCreatedAndLinked()
    {
        var card = await _env.Data.InsertCardAsync(_user, "Cartão Inter", 10, 15);
        _env.Llm.Then(Extraction).Then("59");

        var (status, _) = await _env.Post("/webhook-automate", Body("Compra aprovada no cartão final 1234"));

        status.ShouldBe(HttpStatusCode.OK);
        var invoices = await _env.QueryAsync<(int Id, int Card, string Status)>("SELECT id, conta_id, status FROM faturas");
        invoices.Count.ShouldBe(1);
        invoices[0].Card.ShouldBe(card);
        invoices[0].Status.ShouldBe("Aberta");
        var pending = JsonDocument.Parse(_env.Cache.Values[$"pending_tx:{AutomationEnv.Whatsapp}:ab12cd34"]).RootElement;
        pending.GetProperty("conta_id").GetInt32().ShouldBe(card);
        pending.GetProperty("fatura_id").GetInt32().ShouldBe(invoices[0].Id);
    }

    [Fact]
    [Trait("AC", "011-013")]
    public async Task Income_GoesToTheCheckingAccountWithAPositiveValue()
    {
        _env.Llm.Then("""{"valor_decimal": 3000, "descricao_bruta": "Salario", "tipo_fluxo": "Renda"}""").Then("60");

        await _env.Post("/webhook-automate", Body("Salario no cartao"));

        var pending = JsonDocument.Parse(_env.Cache.Values[$"pending_tx:{AutomationEnv.Whatsapp}:ab12cd34"]).RootElement;
        pending.GetProperty("conta_id").GetInt32().ShouldBe(_checking);
        pending.GetProperty("categoria_id").GetInt32().ShouldBe(60);
        pending.GetProperty("valor_db").GetDouble().ShouldBe(3000);
        pending.GetProperty("tipo_transacao").GetString().ShouldBe("Renda");
    }

    [Fact]
    [Trait("AC", "011-015")]
    public async Task WithoutRedis_SavesTheTransaction_AndTheBotGetsTheSavedText()
    {
        _env.Cache.Available = false;
        _env.Llm.Then(Extraction).Then("59");

        var (status, body) = await _env.Post("/webhook-automate", Body());

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso"}""");
        var row = (await _env.QueryAsync<(decimal Valor, string Tipo, string? Origem, string Descricao, int? Fatura, int Conta, DateTime Data)>(
            "SELECT valor, tipo_transacao, origem, descricao, fatura_id, conta_id, data_transacao FROM transacoes")).Single();
        row.Valor.ShouldBe(-45.90m);
        row.Tipo.ShouldBe("Despesa");
        row.Origem.ShouldBeNull();
        row.Descricao.ShouldBe("Mercado");
        row.Fatura.ShouldBeNull();
        row.Conta.ShouldBe(_checking);
        DateOnly.FromDateTime(row.Data).ShouldBe(_env.Today);
        _env.Bot.Sent.Single().Message.ShouldBe("Transacao salva!\n\nDescricao: Mercado\nValor: R$ 45,90\nCategoria: Despesas Gerais -> Outros");
    }

    [Fact]
    [Trait("AC", "011-016")]
    public async Task WhenRedisCannotStore_SavesWithoutNotifying()
    {
        _env.Cache.FailWrites = true;
        _env.Llm.Then(Extraction).Then("59");

        var (status, body) = await _env.Post("/webhook-automate", Body());

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso"}""");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(1);
        _env.Bot.Sent.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "011-010")]
    public async Task UnknownKey_Is401_WithoutCallingTheGemini()
    {
        var (status, body) = await _env.Post("/webhook-automate", Body(key: "outra"));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe("""{"status":"erro","mensagem":"API key invalida"}""");
        _env.Llm.Requests.ShouldBeEmpty();
    }

    [Theory]
    [Trait("AC", "011-010")]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"texto":"x"}""")]
    [InlineData("""{"user_api_key":"x"}""")]
    [InlineData("""{"texto":"","user_api_key":"x"}""")]
    [InlineData("[]")]
    [InlineData("não é json")]
    public async Task MissingData_Is400(string? json)
    {
        var (status, body) = await _env.Post("/webhook-automate", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"status":"erro","mensagem":"Dados faltando"}""");
    }

    [Fact]
    [Trait("AC", "011-006")]
    public async Task BodyWithoutJsonContentType_StillWorks()
    {
        _env.Llm.Then(Extraction).Then("59");

        var (status, _) = await _env.SendAsync(HttpMethod.Post, "/webhook-automate", Body(), contentType: "text/plain");

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("AC", "011-001")]
    public async Task AKeyStoredWithFernet_AuthenticatesWithTheClearKey()
    {
        var token = new FernetEncryptionService(AuthFixtures.FernetKey, TimeProvider.System).Encrypt("a-chave-em-claro-0123456789012345678901234567890");
        await _env.ScalarAsync<int>("UPDATE usuarios SET api_key_automate = @token WHERE id = @user RETURNING id", new { token, user = _user });
        _env.Llm.Then(Extraction).Then("59");

        var (status, _) = await _env.Post("/webhook-automate", Body(key: "a-chave-em-claro-0123456789012345678901234567890"));
        var (statusToken, _) = await _env.Post("/webhook-automate", Body(key: token));

        status.ShouldBe(HttpStatusCode.OK);
        statusToken.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    [Trait("AC", "011-003")]
    public async Task AnInactiveUser_StillAuthenticates()
    {
        await _env.ScalarAsync<int>("UPDATE usuarios SET ativo = false WHERE id = @user RETURNING id", new { user = _user });
        _env.Llm.Then(Extraction).Then("59");

        (await _env.Post("/webhook-automate", Body())).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [Trait("AC", "011-005")]
    [InlineData("isto não é json")]
    [InlineData("""{"descricao_bruta": 5, "valor_decimal": 1}""")]
    public async Task UnreadableExtraction_Is500WithTheFixedText_AndNothingLeaks(string response)
    {
        _env.Llm.Then(response);

        var (status, body) = await _env.Post("/webhook-automate", Body());

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"Erro ao processar a notificação. Tente novamente."}""");
        body.ShouldNotContain(AutomationEnv.UserKey);
        _env.Logs.All.ShouldNotContain(AutomationEnv.UserKey);
        _env.Logs.All.ShouldNotContain("45,90");
        _env.Logs.All.ShouldNotContain(AutomationEnv.Whatsapp);
        _env.Logs.Entries.ShouldContain(e => e.Message.Contains("Erro no /webhook-automate"));
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
        _env.Cache.Values.ShouldBeEmpty();
    }

    [Theory]
    [Trait("AC", "011-017")]
    [InlineData("""{"valor_decimal": null}""", "Valor invalido na notificacao: deve ser positivo")]
    [InlineData("""{"valor_decimal": 0, "descricao_bruta": "X"}""", "Valor invalido na notificacao: deve ser positivo")]
    [InlineData("""{"valor_decimal": 5, "tipo_fluxo": "Receita"}""", "tipo_fluxo invalido na notificacao. Use: Renda ou Despesa")]
    public async Task InvalidFlowOrValue_Is400_WithNothingSavedOrPending(string response, string message)
    {
        // Desvio 097: o Flask respondia 500 (ou criava a pendência de R$ 0,00).
        _env.Llm.Then(response);

        var (status, body) = await _env.Post("/webhook-automate", Body());

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe($$"""{"status":"erro","mensagem":"{{message}}"}""");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
        _env.Cache.Values.ShouldBeEmpty();
        _env.Bot.Sent.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "011-019")]
    public async Task GeminiKeyFailure_ShowsTheLiteralUserText()
    {
        _env.Llm.ThenThrow(new LlmKeyUnavailableException("⚙️ Você ainda não configurou suas preferências para gemini."));

        var (status, body) = await _env.Post("/webhook-automate", Body());

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"⚙️ Você ainda não configurou suas preferências para gemini."}""");
    }

    [Theory]
    [Trait("AC", "011-019")]
    [InlineData(typeof(LlmQuotaExceededException), "429 Quota exceeded - Gemini API rate limit reached")]
    [InlineData(typeof(LlmPermissionDeniedException), "403 Permission denied - Invalid API key or permissions")]
    public async Task GeminiQuotaAndPermission_ShowTheirLiteralTexts(Type type, string message)
    {
        _env.Llm.ThenThrow((Exception)Activator.CreateInstance(type, message)!);

        var (status, body) = await _env.Post("/webhook-automate", Body());

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe($$"""{"status":"erro","mensagem":"{{message}}"}""");
    }

    [Fact]
    [Trait("AC", "011-019")]
    public async Task OtherGeminiFailure_NeverExposesItsMessage()
    {
        _env.Llm.ThenThrow(new LlmException($"https://x?key={Sentinel}"));

        var (status, body) = await _env.Post("/webhook-automate", Body());

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"Erro ao processar a notificação. Tente novamente."}""");
        body.ShouldNotContain(Sentinel);
        _env.Logs.All.ShouldNotContain(Sentinel);
    }

    [Fact]
    [Trait("AC", "011-004")]
    public async Task OtherMethodAndTrailingSlash()
    {
        (await _env.SendAsync(HttpMethod.Get, "/webhook-automate")).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await _env.Post("/webhook-automate/", Body())).Status.ShouldBe(HttpStatusCode.NotFound);
    }
}

/// <summary><c>POST /api/transacao</c> (spec 011 §3.2).</summary>
[Trait("Category", "Docker")]
public class ApiTransacaoEndpointTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
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

    private static string Body(string account = "Nubank", string payment = "pix", string? description = "2 sorvetes", string value = "15.9") =>
        "{" + $"\"user_api_key\":\"{AutomationEnv.UserKey}\",\"valor\":{value},\"local\":\"Sorveteria\",\"conta\":\"{account}\",\"tipo_pagamento\":\"{payment}\""
        + (description is null ? string.Empty : $",\"descricao\":\"{description}\"") + "}";

    [Fact]
    [Trait("AC", "011-038")]
    public async Task Pending_Returns200WithTheExactBody_AndStoresThePendingInRedis()
    {
        _env.Llm.Then("59");

        var (status, body) = await _env.Post("/api/transacao", Body());

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"success","message":"Transacao pendente de confirmacao no WhatsApp","transaction_id":"ab12cd34","categoria_sugerida":"Despesas Gerais -> Outros","conta_utilizada":"Nubank","vinculado_fatura":false}""");

        var pending = JsonDocument.Parse(_env.Cache.Values[$"pending_tx:{AutomationEnv.Whatsapp}:ab12cd34"]).RootElement;
        pending.EnumerateObject().Select(p => p.Name).ShouldBe(
        [
            "usuario_id", "conta_id", "conta_nome", "conta_tipo", "categoria_id", "fatura_id", "local", "descricao", "descricao_final", "valor_db",
            "valor_original", "tipo_transacao", "tipo_pagamento", "data_transacao", "origem", "budget_warning",
        ]);
        pending.GetProperty("descricao_final").GetString().ShouldBe("Sorveteria - 2 sorvetes");
        pending.GetProperty("valor_db").GetDouble().ShouldBe(-15.9);
        pending.GetProperty("origem").GetString().ShouldBe("api_endpoint");
        pending.GetProperty("budget_warning").ValueKind.ShouldBe(JsonValueKind.Null);
        _env.Cache.Values[$"last_pending:{AutomationEnv.Whatsapp}"].ShouldBe("\"ab12cd34\"");
        _env.Bot.Sent.Single().Number.ShouldBe(AutomationEnv.Whatsapp);
        _env.Bot.Sent.Single().Message.ShouldContain("🏦 Nubank (PIX)");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(0);
        _env.Llm.Requests.Single().Prompt.ShouldContain("A transação teve a descrição: \"Sorveteria - 2 sorvetes\"; O tipo é: \"Despesa\"");
    }

    [Fact]
    [Trait("AC", "011-036")]
    public async Task CreditOnACard_LinksTheCurrentInvoice()
    {
        var card = await _env.Data.InsertCardAsync(_user, "Cartão Inter", 10, 15);
        _env.Llm.Then("59");

        var (status, body) = await _env.Post("/api/transacao", Body("Cartão Inter", "Crédito"));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"vinculado_fatura\":true");
        var invoice = (await _env.QueryAsync<(int Id, int Card)>("SELECT id, conta_id FROM faturas")).Single();
        invoice.Card.ShouldBe(card);
        JsonDocument.Parse(_env.Cache.Values[$"pending_tx:{AutomationEnv.Whatsapp}:ab12cd34"]).RootElement.GetProperty("fatura_id").GetInt32().ShouldBe(invoice.Id);
    }

    [Fact]
    [Trait("AC", "011-036")]
    public async Task CreditOnANonCard_DoesNotLink()
    {
        _env.Llm.Then("59");

        var (_, body) = await _env.Post("/api/transacao", Body("Nubank", "credito"));

        body.ShouldContain("\"vinculado_fatura\":false");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM faturas")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "011-037")]
    public async Task ABreachedPot_AddsTheWarningWithoutBlocking()
    {
        await _env.InsertPotAsync(_user, "Gastos gerais", 20m, "MENSAL", 59);
        await _env.Data.InsertTransactionAsync(_user, _checking, "Despesa", -10m, _env.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), subcategoryId: 59);
        _env.Llm.Then("59");

        var (status, _) = await _env.Post("/api/transacao", Body());

        status.ShouldBe(HttpStatusCode.OK);
        var warning = JsonDocument.Parse(_env.Cache.Values[$"pending_tx:{AutomationEnv.Whatsapp}:ab12cd34"]).RootElement.GetProperty("budget_warning").GetString()!;
        warning.ShouldBe(
            "Atenção: Esta despesa ultrapassará seu limite!\n\nPote: Gastos gerais\nLimite: R$ 20.00\nGasto atual: R$ 10.00\n"
            + "Após transação: R$ 25.90 (129.5% do limite)\nExcesso: R$ 5.90\n\nDeseja prosseguir mesmo assim?");
        _env.Bot.Sent.Single().Message.ShouldContain("⚠️ *AVISO DE ORÇAMENTO*");
    }

    [Fact]
    [Trait("AC", "011-039")]
    public async Task WithoutRedis_SavesTheTransactionAndAnswersTheRedisUnavailableText()
    {
        _env.Cache.Available = false;
        _env.Llm.Then("59");

        var (status, body) = await _env.Post("/api/transacao", Body());

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","message":"Transacao salva com sucesso (Redis indisponivel)","categoria_sugerida":"Despesas Gerais -> Outros"}""");
        var row = (await _env.QueryAsync<(decimal Valor, string Tipo, string? Origem, string Descricao, int Conta)>(
            "SELECT valor, tipo_transacao, origem, descricao, conta_id FROM transacoes")).Single();
        row.ShouldBe((-15.90m, "Despesa", null, "Sorveteria - 2 sorvetes", _checking));
        _env.Bot.Sent.Single().Message.ShouldBe("Transacao salva!\n\nSorveteria - 2 sorvetes\nR$ 15,90\nDespesas Gerais -> Outros");
    }

    [Fact]
    [Trait("AC", "011-039")]
    public async Task WhenRedisCannotStore_SavesAndAnswersTheRedisErrorText()
    {
        _env.Cache.FailWrites = true;
        _env.Llm.Then("59");

        var (status, body) = await _env.Post("/api/transacao", Body());

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"success","message":"Transacao salva (erro no Redis)","transaction_id":null}""");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM transacoes")).ShouldBe(1);
        _env.Bot.Sent.ShouldBeEmpty();
    }

    [Theory]
    [Trait("AC", "011-030")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task InvalidBody_Is400WithTheBareErroKey(string? json)
    {
        var (status, body) = await _env.Post("/api/transacao", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"erro":"JSON invalido ou ausente"}""");
    }

    [Fact]
    [Trait("AC", "011-031")]
    public async Task MissingFields_ListsEveryOne()
    {
        var (status, body) = await _env.Post("/api/transacao", """{"valor":0,"local":"","conta":"<>"}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"status":"erro","mensagem":"Campos obrigatorios faltando: user_api_key, valor, local, conta, tipo_pagamento"}""");
    }

    [Theory]
    [Trait("AC", "011-032")]
    [InlineData("\"abc\"", "Valor invalido: could not convert string to float: 'abc'")]
    [InlineData("-5", "Valor invalido: Valor deve ser positivo")]
    [InlineData("1000000001", "Valor invalido: Valor muito alto (max: 1 bilhao)")]
    [InlineData("[1]", "Valor invalido: float() argument must be a string or a real number, not 'list'")]
    public async Task InvalidValue_Is400WithThePythonText(string value, string message)
    {
        var (status, body) = await _env.Post("/api/transacao", Body(value: value));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe($$"""{"status":"erro","mensagem":"{{message}}"}""");
    }

    [Fact]
    [Trait("AC", "011-033")]
    public async Task InvalidPaymentType_Is400()
    {
        var (status, body) = await _env.Post("/api/transacao", Body(payment: "boleto"));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"status":"erro","mensagem":"tipo_pagamento invalido. Use: credito, debito, pix, dinheiro"}""");
    }

    [Fact]
    [Trait("AC", "011-034")]
    public async Task UnknownKey_Is401()
    {
        var (status, body) = await _env.Post("/api/transacao", Body().Replace(AutomationEnv.UserKey, "outra", StringComparison.Ordinal));

        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe("""{"status":"erro","mensagem":"API key invalida"}""");
    }

    [Fact]
    [Trait("AC", "011-035")]
    public async Task UnknownAccount_Is400_AndInactiveAccountsAreInvisible()
    {
        await _env.Data.InsertAccountAsync(_user, "Antiga", ativa: false);

        var (status, body) = await _env.Post("/api/transacao", Body("Antiga"));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"status":"erro","mensagem":"Conta 'Antiga' nao encontrada. Verifique o nome exato."}""");
    }

    [Fact]
    [Trait("AC", "011-035")]
    public async Task AccountIsFoundByPartialNameAndByType()
    {
        await _env.Data.InsertCardAsync(_user, "Cartão Inter");
        _env.Llm.Then("59").Then("59");

        var (_, byName) = await _env.Post("/api/transacao", Body("nubank"));
        var (_, byType) = await _env.Post("/api/transacao", Body("Crédito"));

        byName.ShouldContain("\"conta_utilizada\":\"Nubank\"");
        byType.ShouldContain("\"conta_utilizada\":\"Cartão Inter\"");
    }

    [Fact]
    [Trait("AC", "011-041")]
    public async Task NonTextField_Is400()
    {
        // Desvio 097: o Flask quebrava em sanitize_input (500).
        var (status, body) = await _env.Post("/api/transacao", Body().Replace("\"Sorveteria\"", "5", StringComparison.Ordinal));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("""{"status":"erro","mensagem":"Campo 'local' deve ser texto"}""");
    }

    [Fact]
    [Trait("AC", "011-005")]
    public async Task UnexpectedError_NeverLeaksTheKeyOrTheException()
    {
        _env.Llm.ThenThrow(new InvalidOperationException($"segredo {AutomationEnv.UserKey}"));

        var (status, body) = await _env.Post("/api/transacao", Body());

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"Erro ao registrar a transação. Tente novamente."}""");
        _env.Logs.All.ShouldNotContain(AutomationEnv.UserKey);
        _env.Logs.Entries.ShouldContain(e => e.Message.Contains("InvalidOperationException") && e.Message.Contains("/api/transacao"));
    }

    [Fact]
    [Trait("AC", "011-004")]
    public async Task TrailingSlash_And_OtherMethod()
    {
        (await _env.Post("/api/transacao/", Body())).Status.ShouldBe(HttpStatusCode.NotFound);
        (await _env.SendAsync(HttpMethod.Get, "/api/transacao")).Status.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
