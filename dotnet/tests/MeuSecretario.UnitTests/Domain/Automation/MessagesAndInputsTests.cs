using System.Text.Json;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.UnitTests.Domain.Automation;

public class AutomationPromptsTests
{
    private static IReadOnlyList<CategoryOption> Categories(JsonElement list) =>
        [.. list.EnumerateArray().Select(e => new CategoryOption(e.GetProperty("id").GetInt32(), e.GetProperty("nome_sub").GetString()!, e.GetProperty("nome_macro").GetString()!))];

    [Fact]
    [Trait("AC", "011-148")]
    public void Prompts_MatchGeminiServiceByteForByte()
    {
        var cases = AutomationFixture.Cases("prompts");
        cases.Count.ShouldBe(4);
        foreach (var c in cases)
        {
            var args = c.GetProperty("args");
            var expected = c.GetProperty("prompt").GetString();
            switch (c.GetProperty("fn").GetString())
            {
                case "extract_from_notification":
                    AutomationPrompts.ExtractFromNotification(args.GetProperty("texto").GetString()!).ShouldBe(expected);
                    break;
                case "categorize_transaction":
                    var fallback = args.GetProperty("fallback");
                    AutomationPrompts.Categorize(
                        Categories(args.GetProperty("categories")),
                        args.GetProperty("descricao").GetString(),
                        args.GetProperty("tipo").GetString()!,
                        fallback.ValueKind == JsonValueKind.Null ? null : fallback.GetInt32()).ShouldBe(expected);
                    break;
                case "extract_transaction_details":
                    AutomationPrompts.ExtractTransactionDetails(args.GetProperty("texto").GetString()!, args.GetProperty("intent").GetString()!).ShouldBe(expected);
                    break;
                default:
                    throw new InvalidOperationException("fn desconhecida");
            }
        }
    }
}

public class ConfirmationMessageTests
{
    [Fact]
    [Trait("AC", "011-144")]
    public void Build_MatchesFormatConfirmationMessageForEveryScenario()
    {
        var categories = new List<CategoryOption> { new(1, "Supermercado", "Alimentação"), new(2, "Outros", "Despesas Gerais") };
        var cases = AutomationFixture.Cases("confirmation");
        cases.Count.ShouldBeGreaterThan(8);
        foreach (var c in cases)
        {
            var d = c.GetProperty("data");
            var pending = ToPending(d);
            ConfirmationMessage.Build(pending, categories, c.GetProperty("id").GetString()!)
                .ShouldBe(c.GetProperty("text").GetString(), c.GetProperty("name").GetString());
        }
    }

    [Fact]
    [Trait("AC", "011-144")]
    public void Build_InstallmentsShowTotalAndFirstInstallment()
    {
        var pending = new PendingTransaction(1, 3, 1, 9, -33.33, 33.33, "Despesa", "2026-10-05", "api_endpoint", null, "Loja", "Cartão", "Cartão de Crédito", "Loja", "credito");
        var text = ConfirmationMessage.Build(pending, [new CategoryOption(1, "Sub", "Macro")], "id", installments: 3, totalValue: 99.99);
        text.ShouldContain("💵 *R$ 99,99* (3x de R$ 33,33)\n");
        text.ShouldContain("💳 Cartão (1ª parcela na fatura atual)\n");
    }

    [Fact]
    [Trait("AC", "011-144")]
    public void ToData_KeepsTheKeysAndOrderOfTheFlaskDict()
    {
        var automate = PendingTransaction.FromAutomate(1, 3, 7, null, "Mercado", -45.9, 45.9, "Despesa", new DateOnly(2026, 10, 5));
        automate.ToData().Keys.ShouldBe(
            ["usuario_id", "conta_id", "categoria_id", "fatura_id", "descricao", "valor_db", "valor_original", "tipo_transacao", "data_transacao", "origem"]);
        automate.ToData()["data_transacao"].ShouldBe("2026-10-05");
        automate.ToData()["origem"].ShouldBe("automate");

        var api = PendingTransaction.FromApiEndpoint(1, 3, "Nubank", "Conta Corrente", 7, 9, "Padaria", null, "Padaria", -8, 8, "pix", new DateOnly(2026, 10, 5), null);
        api.ToData().Keys.ShouldBe(
            [
                "usuario_id", "conta_id", "conta_nome", "conta_tipo", "categoria_id", "fatura_id", "local", "descricao", "descricao_final", "valor_db",
                "valor_original", "tipo_transacao", "tipo_pagamento", "data_transacao", "origem", "budget_warning",
            ]);
        api.ToData()["origem"].ShouldBe("api_endpoint");
        api.ToData()["tipo_transacao"].ShouldBe("Despesa");
    }

    private static PendingTransaction ToPending(JsonElement d)
    {
        string? Text(string name) => d.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        int? InvoiceId() => d.GetProperty("fatura_id").ValueKind == JsonValueKind.Null ? null : d.GetProperty("fatura_id").GetInt32();
        var fromApi = d.TryGetProperty("local", out _);
        return new PendingTransaction(
            d.GetProperty("usuario_id").GetInt32(), d.GetProperty("conta_id").GetInt32(), d.GetProperty("categoria_id").GetInt32(), InvoiceId(),
            d.GetProperty("valor_db").GetDouble(), d.GetProperty("valor_original").GetDouble(), d.GetProperty("tipo_transacao").GetString()!,
            d.GetProperty("data_transacao").GetString()!, d.GetProperty("origem").GetString()!, Text("descricao"), Text("local"),
            Text("conta_nome"), Text("conta_tipo"), Text("descricao_final"), Text("tipo_pagamento"), Text("budget_warning"), fromApi);
    }
}

public class AutomationTextsTests
{
    [Fact]
    [Trait("AC", "011-061")]
    public void BillSettled_MatchesMarkBillAsPaidResponse()
    {
        foreach (var c in AutomationFixture.Cases("paid_message"))
        {
            var text = AutomationTexts.BillSettled(
                c.GetProperty("descricao").GetString()!, MoneyText.Brl(c.GetProperty("valor").GetDouble()), c.GetProperty("categoria").GetString()!);
            text.ShouldBe(c.GetProperty("text").GetString());
        }
    }

    [Fact]
    public void SavedTexts_UseFlaskWordingWithoutAccents()
    {
        AutomationTexts.AutomateSaved("Mercado", 45.9, "A -> B").ShouldBe("Transacao salva!\n\nDescricao: Mercado\nValor: R$ 45,90\nCategoria: A -> B");
        AutomationTexts.AutomateSaved(null, 1, "X").ShouldContain("Descricao: None");
        AutomationTexts.ApiSaved("Padaria - pao", 8, "A -> B").ShouldBe("Transacao salva!\n\nPadaria - pao\nR$ 8,00\nA -> B");
        AutomationTexts.SmsRegistered("Luz", "R$ 10,00", "swile").ShouldBe(
            "Pagamento Registrado!\n\nLuz\nR$ 10,00\nswile\n\n_Nao encontrei uma conta fixa correspondente, entao registrei como despesa avulsa._");
    }
}

public class ApiTransactionInputTests
{
    private static ApiTransactionParse Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ApiTransactionInput.Parse(document.RootElement);
    }

    private const string Valid = """{"user_api_key":"k","valor":15.9,"local":"Sorveteria","conta":"Nubank","tipo_pagamento":"Crédito","descricao":"2 sorvetes"}""";

    [Fact]
    [Trait("AC", "011-038")]
    public void Valid_ParsesAndSanitizes()
    {
        var ok = Parse(Valid).ShouldBeOfType<ApiTransactionParse.Ok>().Data;
        ok.UserApiKey.ShouldBe("k");
        ok.Value.ShouldBe(15.9);
        ok.Local.ShouldBe("Sorveteria");
        ok.AccountName.ShouldBe("Nubank");
        ok.PaymentType.ShouldBe("credito");
        ok.Description.ShouldBe("2 sorvetes");
        ok.FinalDescription.ShouldBe("Sorveteria - 2 sorvetes");
    }

    [Fact]
    [Trait("AC", "011-042")]
    public void BlankDescriptionBecomesNullAndFinalIsTheLocal()
    {
        var ok = Parse("""{"user_api_key":"k","valor":1,"local":"Padaria","conta":"c","tipo_pagamento":"pix","descricao":"  "}""")
            .ShouldBeOfType<ApiTransactionParse.Ok>().Data;
        ok.Description.ShouldBeNull();
        ok.FinalDescription.ShouldBe("Padaria");
    }

    [Fact]
    [Trait("AC", "011-031")]
    public void MissingFields_ListsAllInFlaskOrderAfterSanitizing()
    {
        Parse("""{"valor":0,"local":"","conta":"<>"}""").ShouldBeOfType<ApiTransactionParse.Missing>().Fields
            .ShouldBe(["user_api_key", "valor", "local", "conta", "tipo_pagamento"]);
        Parse("""{"user_api_key":"k","valor":1,"local":"x","conta":"c"}""").ShouldBeOfType<ApiTransactionParse.Missing>().Fields
            .ShouldBe(["tipo_pagamento"]);
    }

    [Theory]
    [Trait("AC", "011-032")]
    [InlineData("\"abc\"", "could not convert string to float: 'abc'")]
    [InlineData("[1]", "float() argument must be a string or a real number, not 'list'")]
    [InlineData("{\"a\":1}", "float() argument must be a string or a real number, not 'dict'")]
    [InlineData("-5", "Valor deve ser positivo")]
    [InlineData("\"-5\"", "Valor deve ser positivo")]
    [InlineData("\"nan\"", "Valor deve ser positivo")]
    [InlineData("1000000000.01", "Valor muito alto (max: 1 bilhao)")]
    [InlineData("\"inf\"", "Valor muito alto (max: 1 bilhao)")]
    public void InvalidValue_CarriesThePythonReason(string valor, string reason) =>
        Parse($$"""{"user_api_key":"k","valor":{{valor}},"local":"x","conta":"c","tipo_pagamento":"pix"}""")
            .ShouldBeOfType<ApiTransactionParse.InvalidValue>().Reason.ShouldBe(reason);

    [Theory]
    [Trait("AC", "011-032")]
    [InlineData("\"15.90\"", 15.9)]
    [InlineData("true", 1)]
    [InlineData("1000000000", 1_000_000_000)]
    public void ValidValues(string valor, double expected) =>
        Parse($$"""{"user_api_key":"k","valor":{{valor}},"local":"x","conta":"c","tipo_pagamento":"pix"}""")
            .ShouldBeOfType<ApiTransactionParse.Ok>().Data.Value.ShouldBe(expected);

    [Theory]
    [Trait("AC", "011-033")]
    [InlineData("cartao")]
    [InlineData("boleto")]
    public void InvalidPaymentType(string type) =>
        Parse($$"""{"user_api_key":"k","valor":1,"local":"x","conta":"c","tipo_pagamento":"{{type}}"}""").ShouldBeOfType<ApiTransactionParse.InvalidPaymentType>();

    [Theory]
    [Trait("AC", "011-041")]
    [InlineData("""{"user_api_key":"k","valor":1,"local":5,"conta":"c","tipo_pagamento":"pix"}""")]
    [InlineData("""{"user_api_key":"k","valor":1,"local":"x","conta":"c","tipo_pagamento":[1]}""")]
    [InlineData("""{"user_api_key":"k","valor":1,"local":"x","conta":{"a":1},"tipo_pagamento":"pix"}""")]
    [InlineData("""{"user_api_key":"k","valor":1,"local":"x","conta":"c","tipo_pagamento":"pix","descricao":7}""")]
    public void UnexpectedType_ComesBeforeTheMissingList(string json) => Parse(json).ShouldBeOfType<ApiTransactionParse.UnexpectedType>();

    [Theory]
    [Trait("AC", "011-041")]
    [InlineData("""{"user_api_key":"k","valor":1,"local":5,"conta":"c","tipo_pagamento":"pix"}""", "local")]
    [InlineData("""{"user_api_key":"k","valor":1,"local":"x","conta":"c","tipo_pagamento":"pix","descricao":7}""", "descricao")]
    [InlineData("""{"user_api_key":"k","valor":1,"local":"x","conta":{"a":1},"tipo_pagamento":"pix"}""", "conta")]
    [InlineData("""{"user_api_key":"k","valor":1,"local":"x","conta":"c","tipo_pagamento":[1]}""", "tipo_pagamento")]
    public void UnexpectedType_NamesTheField(string json, string field) =>
        Parse(json).ShouldBeOfType<ApiTransactionParse.UnexpectedType>().Field.ShouldBe(field);

    [Fact]
    [Trait("AC", "011-041")]
    public void FalsyNonTextFieldsAreJustMissing() =>
        Parse("""{"user_api_key":"k","valor":1,"local":0,"conta":"c","tipo_pagamento":"pix"}""").ShouldBeOfType<ApiTransactionParse.Missing>().Fields.ShouldBe(["local"]);

    [Fact]
    [Trait("AC", "011-002")]
    public void NonTextKeyPassesValidationButNeverAuthenticates() =>
        Parse("""{"user_api_key":123,"valor":1,"local":"x","conta":"c","tipo_pagamento":"pix"}""")
            .ShouldBeOfType<ApiTransactionParse.Ok>().Data.UserApiKey.ShouldBeNull();

    [Fact]
    [Trait("AC", "011-040")]
    public void LocalIsCutBeforeCleaning() =>
        Parse($$"""{"user_api_key":"k","valor":1,"local":"{{new string('a', 98)}}<xyz","conta":"c","tipo_pagamento":"pix"}""")
            .ShouldBeOfType<ApiTransactionParse.Ok>().Data.Local.ShouldBe(new string('a', 98) + "x");
}

public class NotificationParserTests
{
    [Theory]
    [Trait("AC", "011-017")]
    [InlineData("""{"valor_decimal": 45.9, "descricao_bruta": "Mercado", "tipo_fluxo": "Despesa"}""", "Despesa", "Mercado", 45.9)]
    [InlineData("```json\n{\"valor_decimal\": \"12.5\", \"descricao_bruta\": null, \"tipo_fluxo\": \"Renda\"}\n```", "Renda", null, 12.5)]
    [InlineData("""{"valor_decimal": true}""", "Despesa", null, 1)]
    public void Valid(string text, string flow, string? description, double value)
    {
        var parsed = NotificationParser.Parse(text).ShouldBeOfType<NotificationParse.Ok>().Extraction;
        parsed.FlowType.ShouldBe(flow);
        parsed.Description.ShouldBe(description);
        parsed.Value.ShouldBe(value);
        parsed.IsIncome.ShouldBe(flow == "Renda");
    }

    [Theory]
    [Trait("AC", "011-017")]
    [InlineData("não é json")]
    [InlineData("[1]")]
    [InlineData("1")]
    [InlineData("""{"descricao_bruta": 5, "valor_decimal": 1}""")]
    public void Unreadable(string text) => NotificationParser.Parse(text).ShouldBeOfType<NotificationParse.Unreadable>();

    [Theory]
    [Trait("AC", "011-017")]
    [InlineData("""{"tipo_fluxo": null, "valor_decimal": 1}""")]
    [InlineData("""{"tipo_fluxo": "Receita", "valor_decimal": 1}""")]
    [InlineData("""{"tipo_fluxo": 5}""")]
    public void InvalidFlow_Is400(string text) => NotificationParser.Parse(text).ShouldBeOfType<NotificationParse.InvalidFlow>();

    [Theory]
    [Trait("AC", "011-017")]
    [InlineData("""{"descricao_bruta": "X"}""")]
    [InlineData("""{"valor_decimal": null}""")]
    [InlineData("""{"valor_decimal": "abc"}""")]
    [InlineData("""{"valor_decimal": [1]}""")]
    [InlineData("""{"valor_decimal": "nan"}""")]
    [InlineData("""{"valor_decimal": 0}""")]
    [InlineData("""{"valor_decimal": -3.5}""")]
    [InlineData("""{"valor_decimal": 1e10}""")]
    public void InvalidValue_Is400(string text) => NotificationParser.Parse(text).ShouldBeOfType<NotificationParse.InvalidValue>();
}

public class AutomateChoiceTests
{
    private static readonly List<AccountRef> Accounts =
    [
        new(1, "Carteira", "Dinheiro"),
        new(2, "Nubank", "Conta Corrente"),
        new(3, "Cartão Inter", "Cartão de Crédito"),
    ];

    [Theory]
    [Trait("AC", "011-012")]
    [InlineData("Compra aprovada no CARTAO final 1234", 3)]
    [InlineData("COMPRA R$ 10", 3)]
    [InlineData("Credit purchase", 3)]
    [InlineData("Pix recebido", 2)]
    public void Expense_PicksTheCardOnKeywordsElseCheckingAccount(string text, int expected) =>
        AutomateChoice.ChooseAccount(text, "Despesa", Accounts)!.Id.ShouldBe(expected);

    [Fact]
    [Trait("AC", "011-013")]
    public void Income_NeverPicksTheCard() =>
        AutomateChoice.ChooseAccount("compra no cartao", "Renda", Accounts)!.Id.ShouldBe(2);

    [Fact]
    [Trait("AC", "011-012")]
    public void KeywordWithoutCardFallsBackToCheckingAccount() =>
        AutomateChoice.ChooseAccount("compra", "Despesa", Accounts.Where(a => a.Id != 3).ToList())!.Id.ShouldBe(2);

    [Fact]
    [Trait("AC", "011-014")]
    public void WithoutCheckingAccountUsesTheFirstOne() =>
        AutomateChoice.ChooseAccount("x", "Despesa", [new AccountRef(9, "Carteira", "Dinheiro"), new AccountRef(10, "Outra", "Investimento")])!.Id.ShouldBe(9);

    [Fact]
    [Trait("AC", "011-014")]
    public void NoAccountsMeansNone() => AutomateChoice.ChooseAccount("x", "Despesa", []).ShouldBeNull();
}

public class PyIsoDateTests
{
    [Theory]
    [Trait("AC", "011-066")]
    [InlineData("\"2026-01-15\"", true, 2026, 1, 15)]
    [InlineData("\"20260115\"", true, 2026, 1, 15)]
    [InlineData("\"2028-02-29\"", true, 2028, 2, 29)]
    [InlineData("\"2026-02-30\"", false, 0, 0, 0)]
    [InlineData("\"2026-1-5\"", false, 0, 0, 0)]
    [InlineData("\"15/01/2026\"", false, 0, 0, 0)]
    [InlineData("\"2026-0115\"", false, 0, 0, 0)]
    [InlineData("\"0000-01-01\"", false, 0, 0, 0)]
    [InlineData("\"2026-01-15T10:00\"", false, 0, 0, 0)]
    [InlineData("20260115", false, 0, 0, 0)]
    [InlineData("null", false, 0, 0, 0)]
    public void TryParse(string json, bool ok, int year, int month, int day)
    {
        using var document = JsonDocument.Parse(json);
        PyIsoDate.TryParse(document.RootElement, out var date).ShouldBe(ok);
        if (ok)
        {
            date.ShouldBe(new DateOnly(year, month, day));
        }
    }
}

public class ReserveListQueryTests
{
    [Fact]
    [Trait("AC", "011-102")]
    public void Defaults()
    {
        var ok = ReserveListQuery.Parse(null, null, null, null, null).ShouldBeOfType<ReserveListParse.Ok>().Filter;
        ok.Limit.ShouldBe(100);
        ok.Offset.ShouldBe(0);
        ok.IncludeInReserve.ShouldBeNull();
        ok.Periodicity.ShouldBeNull();
        ok.Category.ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "011-102")]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void IncludeFlagIsTrueOnlyForTheWordTrue(string include, bool expected) =>
        ReserveListQuery.Parse(include, null, null, null, null).ShouldBeOfType<ReserveListParse.Ok>().Filter.IncludeInReserve.ShouldBe(expected);

    [Theory]
    [Trait("AC", "011-103")]
    [InlineData("mensal", "MENSAL")]
    [InlineData("Anual", "ANUAL")]
    public void PeriodicityIsUppercased(string input, string expected) =>
        ReserveListQuery.Parse(null, input, null, null, null).ShouldBeOfType<ReserveListParse.Ok>().Filter.Periodicity.ShouldBe(expected);

    [Fact]
    [Trait("AC", "011-103")]
    public void EmptyPeriodicityIsIgnoredAndUnknownIsRejected()
    {
        ReserveListQuery.Parse(null, string.Empty, null, null, null).ShouldBeOfType<ReserveListParse.Ok>().Filter.Periodicity.ShouldBeNull();
        ReserveListQuery.Parse(null, "semestral", null, null, null).ShouldBeOfType<ReserveListParse.InvalidPeriodicity>();
    }

    [Theory]
    [Trait("AC", "011-105")]
    [InlineData(" 5 ", 5)]
    [InlineData("+5", 5)]
    [InlineData("1_0", 10)]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    public void LimitUsesPythonInt(string limit, long expected) =>
        ReserveListQuery.Parse(null, null, null, limit, null).ShouldBeOfType<ReserveListParse.Ok>().Filter.Limit.ShouldBe(expected);

    [Theory]
    [Trait("AC", "011-105")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("9223372036854775808")]
    public void InvalidLimit(string limit) =>
        ReserveListQuery.Parse(null, null, null, limit, null).ShouldBeOfType<ReserveListParse.InvalidPaging>().Field.ShouldBe("limit");

    [Fact]
    [Trait("AC", "011-105")]
    public void InvalidOffset() =>
        ReserveListQuery.Parse(null, null, null, null, "-1").ShouldBeOfType<ReserveListParse.InvalidPaging>().Field.ShouldBe("offset");

    [Fact]
    [Trait("AC", "011-105")]
    public void PagingComesBeforeThePeriodicityCheck()
    {
        // Desvio 097: toda paginação inválida (conversão ou negativo) é 400 e vem antes da periodicidade.
        ReserveListQuery.Parse(null, "semestral", null, "abc", null).ShouldBeOfType<ReserveListParse.InvalidPaging>();
        ReserveListQuery.Parse(null, "semestral", null, "-1", null).ShouldBeOfType<ReserveListParse.InvalidPaging>();
        ReserveListQuery.Parse(null, "semestral", null, null, null).ShouldBeOfType<ReserveListParse.InvalidPeriodicity>();
    }
}

public class ReserveToggleInputTests
{
    private static ReserveToggleParse Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReserveToggleInput.Parse(document.RootElement);
    }

    [Fact]
    [Trait("AC", "011-080")]
    public void FalseIsAValidInclude()
    {
        var ok = Parse("""{"incluir":false,"api_key":"k"}""").ShouldBeOfType<ReserveToggleParse.Ok>();
        ok.Include.ShouldBe(false);
        ok.ApiKey.ShouldBe("k");
    }

    [Theory]
    [Trait("AC", "011-080")]
    [InlineData("{}")]
    [InlineData("""{"incluir":null,"api_key":"k"}""")]
    [InlineData("""{"incluir":true}""")]
    [InlineData("""{"incluir":true,"api_key":""}""")]
    [InlineData("""{"incluir":true,"api_key":null}""")]
    public void Missing(string json) => Parse(json).ShouldBeOfType<ReserveToggleParse.MissingFields>();

    [Fact]
    [Trait("AC", "011-080")]
    public void NullBodyIsMissing() => ReserveToggleInput.Parse(null).ShouldBeOfType<ReserveToggleParse.MissingFields>();

    [Theory]
    [Trait("AC", "011-084")]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void NonBooleanIncludeIsFlagged(string include) =>
        Parse($$"""{"incluir":{{include}},"api_key":"k"}""").ShouldBeOfType<ReserveToggleParse.Ok>().Include.ShouldBeNull();

    [Fact]
    [Trait("AC", "011-002")]
    public void NonTextKeyNeverAuthenticates() =>
        Parse("""{"incluir":true,"api_key":123}""").ShouldBeOfType<ReserveToggleParse.Ok>().ApiKey.ShouldBeNull();
}

public class InvoiceScheduleRulesTests
{
    [Theory]
    [Trait("AC", "011-146")]
    [InlineData(2026, 4, 15, 15)]
    [InlineData(2026, 2, 31, 28)]
    [InlineData(2028, 2, 31, 29)]
    [InlineData(2026, 12, 31, 31)]
    public void TransactionDate_ClampsToTheLastDay(int year, int month, int day, int expectedDay) =>
        InvoiceScheduleRules.TransactionDate(new DateOnly(year, month, 10), day).ShouldBe(new DateOnly(year, month, expectedDay));

    [Fact]
    [Trait("AC", "011-146")]
    public void Description_MarksTheNextInstallment()
    {
        InvoiceScheduleRules.Description("Notebook", "PARCELADO", 2, 3).ShouldBe("Notebook (3/3)");
        InvoiceScheduleRules.Description("Notebook", "PARCELADO", 0, null).ShouldBe("Notebook (1/None)");
        InvoiceScheduleRules.Description("Netflix", "FIXO", 5, 3).ShouldBe("Netflix");
    }

    [Theory]
    [Trait("AC", "011-146")]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, true)]
    [InlineData(2, 3, false)]
    [InlineData(2, null, false)]
    [InlineData(2, 0, false)]
    public void IsLastInstallment_UsesGreaterOrEqual(int next, int? total, bool expected) =>
        InvoiceScheduleRules.IsLastInstallment(next, total).ShouldBe(expected);

    [Fact]
    [Trait("AC", "011-125")]
    public void DetailAmount_IsTextWithTwoDecimalsOrNumberZero()
    {
        InvoiceScheduleRules.DetailAmount(49.90m).ShouldBe("49.90");
        InvoiceScheduleRules.DetailAmount(-49.90m).ShouldBe("49.90");
        InvoiceScheduleRules.DetailAmount(null).ShouldBe(0);
        InvoiceScheduleRules.DetailAmount(0.00m).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "011-122")]
    public void SignedAmount_IncomePositiveExpenseNegative()
    {
        InvoiceScheduleRules.SignedAmount(100m, income: true).ShouldBe(100m);
        InvoiceScheduleRules.SignedAmount(100m, income: false).ShouldBe(-100m);
        InvoiceScheduleRules.SignedAmount(null, income: false).ShouldBe(0m);
    }
}
