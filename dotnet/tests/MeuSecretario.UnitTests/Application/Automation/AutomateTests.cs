using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Transactions;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Automation;

public class ProcessAutomateNotificationTests
{
    private const string Notification = """{"valor_decimal": 45.9, "descricao_bruta": "Mercado", "tipo_fluxo": "Despesa"}""";

    private readonly AutomationHarness _h = new();

    private static AutomateInput Input(string text = "Debito aprovado R$ 45,90", string key = AutomationHarness.Key) => new(false, key, text);

    [Fact]
    [Trait("AC", "011-010")]
    public async Task WithoutGeminiKey_Is503AndNothingIsCalled()
    {
        _h.Settings.GeminiApiKey.Returns(string.Empty);

        (await _h.Automate.ExecuteAsync(Input(), CancellationToken.None)).ShouldBeOfType<AutomateResult.NotConfigured>();

        await _h.Auth.DidNotReceiveWithAnyArgs().AuthenticateAsync(default, default);
        _h.Llm.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "011-010")]
    public async Task MissingData_IsReportedBeforeAuthentication()
    {
        (await _h.Automate.ExecuteAsync(new AutomateInput(true, null, null), CancellationToken.None)).ShouldBeOfType<AutomateResult.MissingData>();

        await _h.Auth.DidNotReceiveWithAnyArgs().AuthenticateAsync(default, default);
    }

    [Fact]
    [Trait("AC", "011-010")]
    public async Task UnknownKey_IsUnauthorizedAndTheGeminiIsNotCalled()
    {
        (await _h.Automate.ExecuteAsync(Input(key: "outra"), CancellationToken.None)).ShouldBeOfType<AutomateResult.Unauthorized>();

        _h.Llm.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "011-011")]
    public async Task Expense_CreatesThePendingTransactionAndAsksForConfirmation_WithoutSaving()
    {
        _h.Llm.Then(Notification).Then("1");

        var result = await _h.Automate.ExecuteAsync(Input(), CancellationToken.None);

        result.ShouldBeOfType<AutomateResult.Pending>().TransactionId.ShouldBe("ab12cd34");
        var data = _h.PendingData();
        data.Keys.ShouldBe(["usuario_id", "conta_id", "categoria_id", "fatura_id", "descricao", "valor_db", "valor_original", "tipo_transacao", "data_transacao", "origem"]);
        data["usuario_id"].ShouldBe(AutomationHarness.UserId);
        data["conta_id"].ShouldBe(2);
        data["categoria_id"].ShouldBe(1);
        data["fatura_id"].ShouldBeNull();
        data["descricao"].ShouldBe("Mercado");
        data["valor_db"].ShouldBe(-45.9);
        data["valor_original"].ShouldBe(45.9);
        data["tipo_transacao"].ShouldBe("Despesa");
        data["data_transacao"].ShouldBe("2026-10-05");
        data["origem"].ShouldBe("automate");

        _h.BotMessage().ShouldContain("*CONFIRME SUA TRANSAÇÃO*");
        _h.BotMessage().ShouldContain("_ID: ab12cd34 | Expira em 5 min_");
        await _h.Bot.Received(1).SendTextAsync(AutomationHarness.WhatsApp, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _h.Posting.DidNotReceiveWithAnyArgs().PostSimpleAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "011-011")]
    public async Task TheGeminiIsCalledWithTheUserAndTheExactPrompts()
    {
        _h.Llm.Then(Notification).Then("1");

        await _h.Automate.ExecuteAsync(Input("Compra aprovada R$ 45,90"), CancellationToken.None);

        _h.Llm.Requests.Count.ShouldBe(2);
        _h.Llm.Requests[0].UserId.ShouldBe(AutomationHarness.UserId);
        _h.Llm.Requests[0].Cache.ShouldBeNull();
        _h.Llm.Requests[0].Prompt.ShouldContain("Analise a notificação: \"Compra aprovada R$ 45,90\"");
        _h.Llm.Requests[1].UserId.ShouldBe(AutomationHarness.UserId);
        _h.Llm.Requests[1].Cache.ShouldBe(LlmCachePolicies.Category);
        _h.Llm.Requests[1].Prompt.ShouldContain("A transação teve a descrição: \"Mercado\"; O tipo é: \"Despesa\"");
    }

    [Theory]
    [Trait("AC", "011-012")]
    [InlineData("Compra no CARTAO final 1234")]
    [InlineData("pagamento crédito parcelado credit")]
    [InlineData("COMPRA aprovada")]
    public async Task ExpenseWithCardWords_UsesTheCardAndLinksTheCurrentInvoice(string text)
    {
        _h.Llm.Then(Notification).Then("1");
        _h.Session.AddInvoice(3, "2026-10-15", "2026-10-10", id: 77);

        await _h.Automate.ExecuteAsync(Input(text), CancellationToken.None);

        var data = _h.PendingData();
        data["conta_id"].ShouldBe(3);
        data["fatura_id"].ShouldBe(77);
        await _h.Invoices.Received(1).EnsureCurrentAsync(AutomationHarness.UserId, 3, Arg.Any<CancellationToken>());
        _h.Session.Committed.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "011-019a")]
    public async Task TheInvoiceIsCreatedAndCommittedBeforeThePendingIsStored()
    {
        _h.Llm.Then(Notification).Then("1");
        var committedWhenStored = false;
        _h.Pending.CreateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                committedWhenStored = _h.Session.Committed;
                return "ab12cd34";
            });

        await _h.Automate.ExecuteAsync(Input("compra no cartao"), CancellationToken.None);

        committedWhenStored.ShouldBeTrue();
        _h.Session.Invoices.Single().Due.ShouldBe(new DateOnly(2026, 10, 15));
    }

    [Fact]
    [Trait("AC", "011-012")]
    public async Task KeywordWithoutAnyCard_FallsBackToTheCheckingAccount()
    {
        _h.Accounts.RemoveAll(a => a.Id == 3);
        _h.Llm.Then(Notification).Then("1");

        await _h.Automate.ExecuteAsync(Input("compra"), CancellationToken.None);

        _h.PendingData()["conta_id"].ShouldBe(2);
        await _h.Invoices.DidNotReceiveWithAnyArgs().EnsureCurrentAsync(default, default, default);
    }

    [Fact]
    [Trait("AC", "011-013")]
    public async Task Income_UsesTheCheckingAccountTheIncomeCategoriesAndAPositiveValue()
    {
        _h.Llm.Then("""{"valor_decimal": 3000, "descricao_bruta": "Salario", "tipo_fluxo": "Renda"}""").Then("5");

        await _h.Automate.ExecuteAsync(Input("Salario recebido no cartao"), CancellationToken.None);

        var data = _h.PendingData();
        data["conta_id"].ShouldBe(2);
        data["categoria_id"].ShouldBe(5);
        data["valor_db"].ShouldBe(3000.0);
        data["valor_original"].ShouldBe(3000.0);
        data["tipo_transacao"].ShouldBe("Renda");
        await _h.Lookups.Received(1).ListCategoriesAsync(AutomationHarness.UserId, true, Arg.Any<CancellationToken>());
        await _h.CategoryLookup.Received(1).FindFallbackAsync(true, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "011-014")]
    public async Task WithoutActiveAccounts_FailsBeforeStoringAnything()
    {
        _h.Accounts.Clear();
        _h.Llm.Then(Notification);

        await Should.ThrowAsync<AutomationFailureException>(() => _h.Automate.ExecuteAsync(Input(), CancellationToken.None));

        await _h.Pending.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "011-015")]
    public async Task WithoutRedis_SavesDirectlyAndNotifiesTheSavedText()
    {
        _h.Cache.Unavailable = true;
        _h.Llm.Then(Notification).Then("1");

        var result = await _h.Automate.ExecuteAsync(Input(), CancellationToken.None);

        result.ShouldBeOfType<AutomateResult.Saved>();
        var posted = _h.Posted();
        posted.Kind.ShouldBe(TransactionKind.Expense);
        posted.Amount.ShouldBe(45.9m);
        posted.AccountId.ShouldBe(2);
        posted.SubcategoryId.ShouldBe(1);
        posted.Description.ShouldBe("Mercado");
        posted.Date.ShouldBe(AutomationHarness.Today);
        posted.Origin.ShouldBeNull();
        _h.BotMessage().ShouldBe("Transacao salva!\n\nDescricao: Mercado\nValor: R$ 45,90\nCategoria: Alimentação -> Supermercado");
        await _h.Pending.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "011-015")]
    public async Task WithoutRedis_AnIncomeIsPostedAsIncome()
    {
        _h.Cache.Unavailable = true;
        _h.Llm.Then("""{"valor_decimal": 100, "descricao_bruta": "Venda", "tipo_fluxo": "Renda"}""").Then("5");

        await _h.Automate.ExecuteAsync(Input(), CancellationToken.None);

        _h.Posted().Kind.ShouldBe(TransactionKind.Income);
    }

    [Fact]
    [Trait("AC", "011-016")]
    public async Task WhenThePendingCannotBeStored_SavesDirectlyWithoutNotifying()
    {
        _h.Pending.CreateAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns((string?)null);
        _h.Llm.Then(Notification).Then("1");

        var result = await _h.Automate.ExecuteAsync(Input(), CancellationToken.None);

        result.ShouldBeOfType<AutomateResult.Saved>();
        _h.Posted().Amount.ShouldBe(45.9m);
        await _h.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Theory]
    [Trait("AC", "011-017")]
    [InlineData("não é json")]
    [InlineData("[1]")]
    [InlineData("""{"valor_decimal": null}""")]
    [InlineData("""{"valor_decimal": "abc"}""")]
    [InlineData("""{"tipo_fluxo": "Receita", "valor_decimal": 1}""")]
    public async Task InvalidExtraction_FailsAndNothingIsSavedOrPending(string response)
    {
        _h.Llm.Then(response);

        await Should.ThrowAsync<AutomationFailureException>(() => _h.Automate.ExecuteAsync(Input(), CancellationToken.None));

        await _h.Pending.DidNotReceiveWithAnyArgs().CreateAsync(default!, default!, default);
        await _h.Posting.DidNotReceiveWithAnyArgs().PostSimpleAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "011-017")]
    public async Task EmptyGeminiResponse_Fails() =>
        await Should.ThrowAsync<LlmException>(() =>
        {
            _h.Llm.Then(string.Empty);
            return _h.Automate.ExecuteAsync(Input(), CancellationToken.None);
        });

    [Fact]
    [Trait("AC", "011-017")]
    public async Task MissingFlowTypeMeansExpense()
    {
        _h.Llm.Then("""{"valor_decimal": 10, "descricao_bruta": "X"}""").Then("1");

        await _h.Automate.ExecuteAsync(Input(), CancellationToken.None);

        _h.PendingData()["tipo_transacao"].ShouldBe("Despesa");
    }

    [Fact]
    [Trait("AC", "011-019")]
    public async Task GeminiKeyFailures_PropagateWithTheirOwnType()
    {
        _h.Llm.ThenThrow(new LlmKeyUnavailableException("⚙️ Você ainda não configurou suas preferências para gemini."));

        var ex = await Should.ThrowAsync<LlmKeyUnavailableException>(() => _h.Automate.ExecuteAsync(Input(), CancellationToken.None));

        ex.Message.ShouldStartWith("⚙️ Você ainda não configurou");
    }

    [Fact]
    [Trait("AC", "011-018")]
    public async Task BlockedCategorization_UsesTheFallbackCategory()
    {
        _h.Llm.Then(Notification).ThenThrow(new LlmBlockedException("bloqueada"));

        await _h.Automate.ExecuteAsync(Input(), CancellationToken.None);

        _h.PendingData()["categoria_id"].ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "011-014")]
    public async Task NonTextNotification_Fails()
    {
        await Should.ThrowAsync<AutomationFailureException>(
            () => _h.Automate.ExecuteAsync(new AutomateInput(false, AutomationHarness.Key, null), CancellationToken.None));
    }
}
