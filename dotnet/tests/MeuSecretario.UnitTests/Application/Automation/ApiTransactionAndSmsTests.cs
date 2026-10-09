using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Transactions;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Automation;

public class RegisterApiTransactionTests
{
    private const string Valid =
        """{"user_api_key":"chave-do-atalho","valor":15.9,"local":"Sorveteria","conta":"Nubank","tipo_pagamento":"pix","descricao":"2 sorvetes"}""";

    private readonly AutomationHarness _h = new();

    public RegisterApiTransactionTests()
    {
        _h.Lookups.FindAccountByNameOrTypeAsync(AutomationHarness.UserId, "Nubank", Arg.Any<CancellationToken>()).Returns(new AccountRef(2, "Nubank", "Conta Corrente"));
        _h.Lookups.FindAccountByNameOrTypeAsync(AutomationHarness.UserId, "Cartão Inter", Arg.Any<CancellationToken>()).Returns(new AccountRef(3, "Cartão Inter", "Cartão de Crédito"));
    }

    private Task<ApiTransactionResult> Run(string json) => _h.ApiTransaction.ExecuteAsync(AutomationHarness.Json(json), CancellationToken.None);

    [Fact]
    [Trait("AC", "011-030")]
    public async Task WithoutGeminiKey_Is503BeforeTheBody()
    {
        _h.Settings.GeminiApiKey.Returns(string.Empty);

        (await _h.ApiTransaction.ExecuteAsync(null, CancellationToken.None)).ShouldBeOfType<ApiTransactionResult.NotConfigured>();
    }

    [Fact]
    [Trait("AC", "011-030")]
    public async Task MissingOrInvalidBody_IsInvalidJson() =>
        (await _h.ApiTransaction.ExecuteAsync(null, CancellationToken.None)).ShouldBeOfType<ApiTransactionResult.InvalidJson>();

    [Fact]
    [Trait("AC", "011-031")]
    public async Task MissingFields_AreListedAndNothingElseRuns()
    {
        var result = await Run("""{"valor":0,"local":""}""");

        result.ShouldBeOfType<ApiTransactionResult.Missing>().Fields.ShouldBe(["user_api_key", "valor", "local", "conta", "tipo_pagamento"]);
        await _h.Auth.DidNotReceiveWithAnyArgs().AuthenticateAsync(default, default);
    }

    [Fact]
    [Trait("AC", "011-032")]
    public async Task InvalidValue_CarriesThePythonMessage()
    {
        var result = await Run("""{"user_api_key":"k","valor":"abc","local":"x","conta":"c","tipo_pagamento":"pix"}""");

        result.ShouldBeOfType<ApiTransactionResult.InvalidValue>().Reason.ShouldBe("could not convert string to float: 'abc'");
    }

    [Fact]
    [Trait("AC", "011-033")]
    public async Task InvalidPaymentType_IsReported() =>
        (await Run("""{"user_api_key":"k","valor":1,"local":"x","conta":"c","tipo_pagamento":"boleto"}""")).ShouldBeOfType<ApiTransactionResult.InvalidPaymentType>();

    [Fact]
    [Trait("AC", "011-041")]
    public async Task NonTextField_IsAnInvalidInput_NotA500() =>
        // Desvio 097: o Flask quebrava em sanitize_input (500).
        (await Run("""{"user_api_key":"k","valor":1,"local":5,"conta":"c","tipo_pagamento":"pix"}""")).ShouldBeOfType<ApiTransactionResult.Invalid>()
            .Message.ShouldBe("Campo 'local' deve ser texto");

    [Fact]
    [Trait("AC", "011-034")]
    public async Task UnknownKey_IsUnauthorized_BeforeAnyLookup()
    {
        var result = await Run("""{"user_api_key":"outra","valor":1,"local":"x","conta":"c","tipo_pagamento":"pix"}""");

        result.ShouldBeOfType<ApiTransactionResult.Unauthorized>();
        await _h.Lookups.DidNotReceiveWithAnyArgs().FindAccountByNameOrTypeAsync(default, default!, default);
    }

    [Fact]
    [Trait("AC", "011-035")]
    public async Task UnknownAccount_IsReportedWithTheSanitizedName()
    {
        var result = await Run("""{"user_api_key":"chave-do-atalho","valor":1,"local":"x","conta":"  <Banco>  Z ","tipo_pagamento":"pix"}""");

        result.ShouldBeOfType<ApiTransactionResult.AccountNotFound>().Name.ShouldBe("Banco Z");
    }

    [Fact]
    [Trait("AC", "011-038")]
    public async Task Redis_CreatesThePendingAndAsksForConfirmation()
    {
        _h.Llm.Then("1");

        var result = await Run(Valid);

        var pending = result.ShouldBeOfType<ApiTransactionResult.Pending>();
        pending.TransactionId.ShouldBe("ab12cd34");
        pending.CategoryLabel.ShouldBe("Alimentação -> Supermercado");
        pending.AccountName.ShouldBe("Nubank");
        pending.LinkedToInvoice.ShouldBeFalse();

        var data = _h.PendingData();
        data.Keys.ShouldBe(
            [
                "usuario_id", "conta_id", "conta_nome", "conta_tipo", "categoria_id", "fatura_id", "local", "descricao", "descricao_final", "valor_db",
                "valor_original", "tipo_transacao", "tipo_pagamento", "data_transacao", "origem", "budget_warning",
            ]);
        data["conta_nome"].ShouldBe("Nubank");
        data["descricao_final"].ShouldBe("Sorveteria - 2 sorvetes");
        data["valor_db"].ShouldBe(-15.9);
        data["tipo_pagamento"].ShouldBe("pix");
        data["origem"].ShouldBe("api_endpoint");
        data["budget_warning"].ShouldBeNull();

        await _h.Pending.Received(1).SetLastPendingAsync(AutomationHarness.WhatsApp, "ab12cd34", Arg.Any<CancellationToken>());
        _h.BotMessage().ShouldContain("📍 *Sorveteria*");
        _h.BotMessage().ShouldContain("📝 2 sorvetes");
        _h.BotMessage().ShouldContain("🏦 Nubank (PIX)");
        await _h.Posting.DidNotReceiveWithAnyArgs().PostSimpleAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "011-038")]
    public async Task TheCategorizationUsesTheLocalAndDescriptionText()
    {
        _h.Llm.Then("1");

        await Run(Valid);

        _h.Llm.Requests.Single().Prompt.ShouldContain("A transação teve a descrição: \"Sorveteria - 2 sorvetes\"; O tipo é: \"Despesa\"");
        _h.Llm.Requests.Single().UserId.ShouldBe(AutomationHarness.UserId);
    }

    [Fact]
    [Trait("AC", "011-036")]
    public async Task Credit_OnACard_LinksTheCurrentInvoice()
    {
        _h.Llm.Then("1");
        _h.Session.AddInvoice(3, "2026-10-15", "2026-10-10", id: 77);

        var result = await Run("""{"user_api_key":"chave-do-atalho","valor":15.9,"local":"Loja","conta":"Cartão Inter","tipo_pagamento":"Crédito"}""");

        result.ShouldBeOfType<ApiTransactionResult.Pending>().LinkedToInvoice.ShouldBeTrue();
        _h.PendingData()["fatura_id"].ShouldBe(77);
        await _h.Invoices.Received(1).EnsureCurrentAsync(AutomationHarness.UserId, 3, Arg.Any<CancellationToken>());
        // Desvio 100: o emoji do cartão no lugar do U+FFFD do Flask.
        _h.BotMessage().ShouldContain("💳 Cartão Inter (na fatura)");
    }

    [Fact]
    [Trait("AC", "011-036")]
    public async Task Credit_OnANonCard_DoesNotLink()
    {
        _h.Llm.Then("1");

        var result = await Run("""{"user_api_key":"chave-do-atalho","valor":15.9,"local":"Loja","conta":"Nubank","tipo_pagamento":"credito"}""");

        result.ShouldBeOfType<ApiTransactionResult.Pending>().LinkedToInvoice.ShouldBeFalse();
        _h.PendingData()["fatura_id"].ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "011-036")]
    [InlineData("debito")]
    [InlineData("pix")]
    [InlineData("dinheiro")]
    public async Task OtherPaymentTypes_NeverTouchTheInvoice(string payment)
    {
        _h.Llm.Then("1");

        await Run($$"""{"user_api_key":"chave-do-atalho","valor":15.9,"local":"Loja","conta":"Cartão Inter","tipo_pagamento":"{{payment}}"}""");

        await _h.Invoices.DidNotReceiveWithAnyArgs().EnsureCurrentAsync(default, default, default);
        _h.PendingData()["fatura_id"].ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "011-037")]
    public async Task ABreachedPot_AddsTheWarningButDoesNotBlock()
    {
        _h.Llm.Then("1");
        _h.Budget.ListPotsForSubcategoryAsync(AutomationHarness.UserId, 1, Arg.Any<CancellationToken>()).Returns([new PotRow(9, "Mercado", 100, "MENSAL")]);
        _h.Budget.SumPotSpendAsync(AutomationHarness.UserId, 9, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Arg.Any<CancellationToken>()).Returns(90m);

        var result = await Run(Valid);

        result.ShouldBeOfType<ApiTransactionResult.Pending>();
        var warning = _h.PendingData()["budget_warning"].ShouldBeOfType<string>();
        warning.ShouldContain("Pote: Mercado");
        warning.ShouldContain("Após transação: R$ 105.90");
        _h.BotMessage().ShouldContain("⚠️ *AVISO DE ORÇAMENTO*");
    }

    [Fact]
    [Trait("AC", "011-039")]
    public async Task WithoutRedis_SavesAndNotifies()
    {
        _h.Cache.Unavailable = true;
        _h.Llm.Then("1");

        var result = await Run(Valid);

        result.ShouldBeOfType<ApiTransactionResult.SavedWithoutRedis>().CategoryLabel.ShouldBe("Alimentação -> Supermercado");
        var posted = _h.Posted();
        posted.Kind.ShouldBe(TransactionKind.Expense);
        posted.Amount.ShouldBe(15.9m);
        posted.Description.ShouldBe("Sorveteria - 2 sorvetes");
        posted.Origin.ShouldBeNull();
        _h.BotMessage().ShouldBe("Transacao salva!\n\nSorveteria - 2 sorvetes\nR$ 15,90\nAlimentação -> Supermercado");
    }

    [Fact]
    [Trait("AC", "011-039")]
    public async Task WhenThePendingCannotBeStored_SavesWithoutNotifying()
    {
        _h.Llm.Then("1");
        _h.Pending.CreateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await Run(Valid);

        result.ShouldBeOfType<ApiTransactionResult.SavedAfterRedisError>();
        _h.Posted().Amount.ShouldBe(15.9m);
        await _h.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "011-042")]
    public async Task WithoutDescription_TheFinalDescriptionIsTheLocal()
    {
        _h.Cache.Unavailable = true;
        _h.Llm.Then("1");

        await Run("""{"user_api_key":"chave-do-atalho","valor":8,"local":"Padaria","conta":"Nubank","tipo_pagamento":"pix","descricao":" "}""");

        _h.Posted().Description.ShouldBe("Padaria");
    }
}

public class ProcessSmsPaymentTests
{
    private readonly AutomationHarness _h = new();

    public ProcessSmsPaymentTests()
    {
        _h.Lookups.FindSettleTargetAsync(AutomationHarness.UserId, 10, Arg.Any<CancellationToken>()).Returns(new SettleTarget("Luz", 2, 31));
        _h.Lookups.FindAccountIdWithFallbackAsync(AutomationHarness.UserId, "Carteira", Arg.Any<CancellationToken>()).Returns(1);
    }

    private Task<SmsPaymentResult> Run(string json) => _h.Sms.ExecuteAsync(AutomationHarness.Json(json), CancellationToken.None);

    [Fact]
    [Trait("AC", "011-060")]
    public async Task DatabaseDown_IsReportedBeforeTheBody()
    {
        _h.Health.CheckAsync(Arg.Any<CancellationToken>()).Returns(new DatabaseStatus(false, "x"));

        (await _h.Sms.ExecuteAsync(null, CancellationToken.None)).ShouldBeOfType<SmsPaymentResult.DatabaseUnavailable>();
    }

    [Theory]
    [Trait("AC", "011-060")]
    [InlineData("{}")]
    [InlineData("""{"user_api_key":"k","descricao_pagamento":"luz","valor_pago":0,"conta_pagamento":"swile"}""")]
    [InlineData("""{"user_api_key":"k","descricao_pagamento":"","valor_pago":1,"conta_pagamento":"swile"}""")]
    [InlineData("""{"user_api_key":"k","descricao_pagamento":"luz","valor_pago":1}""")]
    public async Task MissingAnyOfTheFour_IsMissingData(string json) => (await Run(json)).ShouldBeOfType<SmsPaymentResult.MissingData>();

    [Fact]
    [Trait("AC", "011-060")]
    public async Task NullBody_IsMissingData() => (await _h.Sms.ExecuteAsync(null, CancellationToken.None)).ShouldBeOfType<SmsPaymentResult.MissingData>();

    [Fact]
    [Trait("AC", "011-060")]
    public async Task UnknownKey_IsUnauthorized() =>
        (await Run("""{"user_api_key":"outra","descricao_pagamento":"luz","valor_pago":1,"conta_pagamento":"swile"}""")).ShouldBeOfType<SmsPaymentResult.Unauthorized>();

    [Fact]
    [Trait("AC", "011-061")]
    public async Task MatchingBill_IsSettledOnTheScheduleAccountAndNotified()
    {
        var result = await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"conta de luz","valor_pago":150.5,"conta_pagamento":"swile","data_pagamento":"2026-10-02"}""");

        var settled = result.ShouldBeOfType<SmsPaymentResult.Settled>();
        settled.BillDescription.ShouldBe("Luz");
        settled.TransactionId.ShouldBe(500);
        var posted = _h.Posted();
        posted.AccountId.ShouldBe(2);
        posted.SubcategoryId.ShouldBe(31);
        posted.Kind.ShouldBe(TransactionKind.Expense);
        posted.Amount.ShouldBe(150.5m);
        posted.Description.ShouldBe("Luz");
        posted.Date.ShouldBe(new DateOnly(2026, 10, 2));
        posted.ScheduleId.ShouldBeNull();
        _h.BotMessage().ShouldBe(
            "✅ *CONTA QUITADA!* ✅\n\n📝 Luz\n💰 R$ 150,50\n📊 Energia\n\n_Esta conta não será mais cobrada automaticamente este mês._");
        _h.Llm.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "011-061")]
    public async Task TheDateDefaultsToToday()
    {
        await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"luz","valor_pago":10,"conta_pagamento":"swile"}""");

        _h.Posted().Date.ShouldBe(AutomationHarness.Today);
    }

    [Fact]
    [Trait("AC", "011-063")]
    public async Task NoMatch_RegistersALooseExpenseOnTheWalletAndNotifies_CallingTheGeminiOnlyToCategorize()
    {
        _h.Llm.Then("1");

        var result = await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"Mercado Extra","valor_pago":80.5,"conta_pagamento":"swile"}""");

        result.ShouldBeOfType<SmsPaymentResult.Registered>();
        // Desvio 096: a extração inútil (extract_transaction_details) saiu; só a categorização chama o Gemini.
        _h.Llm.Requests.Count.ShouldBe(1);
        _h.Llm.Requests[0].Cache.ShouldBe(LlmCachePolicies.Category);
        _h.Llm.Requests[0].UserId.ShouldBe(AutomationHarness.UserId);
        var posted = _h.Posted();
        posted.AccountId.ShouldBe(1);
        posted.SubcategoryId.ShouldBe(1);
        posted.Amount.ShouldBe(80.5m);
        posted.Description.ShouldBe("Mercado Extra (swile)");
        _h.BotMessage().ShouldBe(
            "Pagamento Registrado!\n\nMercado Extra\nR$ 80,50\nswile\n\n_Nao encontrei uma conta fixa correspondente, entao registrei como despesa avulsa._");
    }

    [Fact]
    [Trait("AC", "011-063")]
    public async Task NoMatch_NeverAsksTheGeminiToExtractTheTransaction()
    {
        _h.Llm.Then("1");

        await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"Mercado Extra","valor_pago":80,"conta_pagamento":"swile"}""");

        _h.Llm.Requests.ShouldAllBe(r => !r.Prompt.Contains("Paguei", StringComparison.Ordinal));
        await _h.Posting.Received(1).PostSimpleAsync(Arg.Any<PostSimpleTransaction>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "011-064")]
    public async Task NoAccount_FailsWithoutSaving()
    {
        _h.Lookups.FindAccountIdWithFallbackAsync(AutomationHarness.UserId, "Carteira", Arg.Any<CancellationToken>()).Returns((int?)null);
        _h.Llm.Then("1");

        await Should.ThrowAsync<AutomationFailureException>(
            () => Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"Mercado Extra","valor_pago":80,"conta_pagamento":"swile"}"""));

        await _h.Posting.DidNotReceiveWithAnyArgs().PostSimpleAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "011-065")]
    public async Task TextAmount_SettlesTheBill_AndNotifiesWithTheNumber()
    {
        // Desvio 098: "150.50" como texto grava, avisa o WhatsApp e responde 200 (o Flask gravava e dava 500).
        var result = await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"luz","valor_pago":"150.50","conta_pagamento":"swile"}""");

        result.ShouldBeOfType<SmsPaymentResult.Settled>();
        _h.Posted().Amount.ShouldBe(150.5m);
        _h.BotMessage().ShouldContain("💰 R$ 150,50");
    }

    [Fact]
    [Trait("AC", "011-065")]
    public async Task TextAmount_RegistersALooseExpense_AndNotifiesWithTheNumber()
    {
        _h.Llm.Then("1");

        var result = await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"Mercado Extra","valor_pago":" 80.5 ","conta_pagamento":"swile"}""");

        result.ShouldBeOfType<SmsPaymentResult.Registered>();
        _h.Posted().Amount.ShouldBe(80.5m);
        _h.BotMessage().ShouldBe(
            "Pagamento Registrado!\n\nMercado Extra\nR$ 80,50\nswile\n\n_Nao encontrei uma conta fixa correspondente, entao registrei como despesa avulsa._");
    }

    [Theory]
    [Trait("AC", "011-066")]
    [InlineData("\"2026-02-30\"")]
    [InlineData("\"15/01/2026\"")]
    [InlineData("5")]
    public async Task InvalidDate_Is400WithoutSaving(string date)
    {
        // Desvio 097: o Flask respondia 500 com str(e).
        var result = await Run($$"""{"user_api_key":"chave-do-atalho","descricao_pagamento":"luz","valor_pago":10,"conta_pagamento":"swile","data_pagamento":{{date}}}""");

        result.ShouldBeOfType<SmsPaymentResult.Invalid>().Message.ShouldBe("data_pagamento invalida. Use o formato AAAA-MM-DD");
        await _h.Posting.DidNotReceiveWithAnyArgs().PostSimpleAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "011-066")]
    public async Task CompactIsoDate_IsAccepted()
    {
        await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"luz","valor_pago":10,"conta_pagamento":"swile","data_pagamento":"20260115"}""");

        _h.Posted().Date.ShouldBe(new DateOnly(2026, 1, 15));
    }

    [Theory]
    [Trait("AC", "011-067")]
    [InlineData("-10", "Valor invalido: Valor deve ser positivo")]
    [InlineData("\"-0.01\"", "Valor invalido: Valor deve ser positivo")]
    [InlineData("\"nan\"", "Valor invalido: Valor deve ser positivo")]
    [InlineData("\"abc\"", "Valor invalido: could not convert string to float: 'abc'")]
    [InlineData("[1]", "Valor invalido: float() argument must be a string or a real number, not 'list'")]
    [InlineData("1000000000.01", "Valor invalido: Valor muito alto (max: 1 bilhao)")]
    public async Task InvalidAmount_Is400WithoutSaving(string amount, string message)
    {
        // Desvio 097: o Flask gravava despesa positiva para valor negativo e respondia 500 nos demais casos.
        var result = await Run($$"""{"user_api_key":"chave-do-atalho","descricao_pagamento":"luz","valor_pago":{{amount}},"conta_pagamento":"swile"}""");

        result.ShouldBeOfType<SmsPaymentResult.Invalid>().Message.ShouldBe(message);
        await _h.Posting.DidNotReceiveWithAnyArgs().PostSimpleAsync(default!, default);
        await _h.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "011-068")]
    public async Task ABotThatReturnsFalse_DoesNotFailTheRoute()
    {
        _h.Bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":"luz","valor_pago":10,"conta_pagamento":"swile"}""");

        result.ShouldBeOfType<SmsPaymentResult.Settled>();
    }

    [Fact]
    [Trait("AC", "011-061")]
    public async Task NonTextDescription_Is400() =>
        (await Run("""{"user_api_key":"chave-do-atalho","descricao_pagamento":123,"valor_pago":10,"conta_pagamento":"swile"}""")).ShouldBeOfType<SmsPaymentResult.Invalid>()
            .Message.ShouldBe("Campo 'descricao_pagamento' deve ser texto");
}
