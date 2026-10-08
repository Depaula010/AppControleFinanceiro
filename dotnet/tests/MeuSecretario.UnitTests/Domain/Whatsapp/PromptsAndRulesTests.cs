using System.Text;
using System.Text.Json;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

public class WhatsAppPromptsTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static string Render(string file, string message, string? accountsJson) => file switch
    {
        "intent" => WhatsAppPrompts.Intent(message),
        "transfer-details" => WhatsAppPrompts.TransferDetails(message, accountsJson!),
        "invoice-payment-details" => WhatsAppPrompts.InvoicePaymentDetails(message, accountsJson!),
        "category-query" => WhatsAppPrompts.CategoryQuery(message),
        "period-query" => WhatsAppPrompts.PeriodQuery(message),
        "chart-type" => WhatsAppPrompts.ChartType(message),
        "payment-items" => WhatsAppPrompts.PaymentItems(message),
        "calendar-query" => WhatsAppPrompts.CalendarQuery(message),
        "event-creation" => WhatsAppPrompts.EventCreation(message, Today).Replace(WhatsAppPrompts.EventTodayLine(Today), string.Empty, StringComparison.Ordinal),
        "event-deletion" => WhatsAppPrompts.EventDeletion(message),
        "time-filter" => WhatsAppPrompts.TimeFilter(message),
        "notification-config" => WhatsAppPrompts.NotificationConfig(message),
        "free-time-query" => WhatsAppPrompts.FreeTimeQuery(message),
        "invoice-query" => WhatsAppPrompts.InvoiceQuery(message, accountsJson!),
        "balance-query" => WhatsAppPrompts.BalanceQuery(message, accountsJson!),
        "monthly-report-config" => WhatsAppPrompts.MonthlyReportConfig(message),
        "location-config" => WhatsAppPrompts.LocationConfig(message),
        "address-config" => WhatsAppPrompts.AddressConfig(message),
        "address-deletion" => WhatsAppPrompts.AddressDeletion(message),
        _ => throw new InvalidOperationException("prompt desconhecido: " + file),
    };

    [Fact]
    [Trait("AC", "012-016")]
    public void EveryPrompt_RendersByteForByteLikeGeminiService()
    {
        var cases = WhatsAppFixture.Cases("prompt_cases");
        cases.Count.ShouldBeGreaterThan(80);
        foreach (var c in cases)
        {
            var accounts = c.GetProperty("accounts");
            string? json = null;
            if (accounts.ValueKind == JsonValueKind.Array)
            {
                var refs = accounts.EnumerateArray()
                    .Select(a => new AccountRef(a.TryGetProperty("id", out var id) ? id.GetInt32() : 0, a.GetProperty("nome").GetString()!, a.GetProperty("tipo").GetString()!))
                    .ToList();
                var withId = accounts.GetArrayLength() > 0 && accounts[0].TryGetProperty("id", out _);
                json = AccountsJson.Dump(refs, withId);
                json.ShouldBe(c.GetProperty("json").GetString());
            }

            var file = c.GetProperty("file").GetString()!;
            Render(file, c.GetProperty("message").GetString()!, json).ShouldBe(c.GetProperty("prompt").GetString(), $"prompt {file}");
        }
    }

    [Fact]
    [Trait("AC", "012-016")]
    public void MessageWithAToken_DoesNotContaminateTheAccountsList()
    {
        WhatsAppPrompts.BalanceQuery("oi ⟦CONTAS⟧ tchau", "[]").ShouldContain("oi ⟦CONTAS⟧ tchau");
    }

    [Fact]
    [Trait("AC", "012-016")]
    public void CacheMetadata_MatchesThePythonPolicies()
    {
        foreach (var template in WhatsAppFixture.Cases("prompt_templates"))
        {
            var cache = template.GetProperty("cache");
            var file = template.GetProperty("file").GetString();
            switch (file)
            {
                case "intent":
                    cache.GetProperty("key_prefix").GetString().ShouldBe("intent");
                    cache.GetProperty("ttl").GetInt32().ShouldBe(604800);
                    cache.GetProperty("user_specific").GetBoolean().ShouldBeFalse();
                    break;
                case "period-query":
                    cache.GetProperty("key_prefix").GetString().ShouldBe("period_query");
                    cache.GetProperty("ttl").GetInt32().ShouldBe(604800);
                    break;
                case "chart-type":
                    cache.GetProperty("key_prefix").GetString().ShouldBe("chart_type");
                    cache.GetProperty("ttl").GetInt32().ShouldBe(604800);
                    break;
                default:
                    cache.ValueKind.ShouldBe(JsonValueKind.Null, $"{file} não usa cache");
                    break;
            }
        }
    }

    [Fact]
    [Trait("AC", "012-016")]
    public void EventCreation_AddsTodayLine_AfterTheFirstLine()
    {
        var prompt = WhatsAppPrompts.EventCreation("academia amanhã 7h", new DateOnly(2026, 10, 7));

        prompt.ShouldStartWith("Analise a mensagem de criação de evento: \"academia amanhã 7h\"\n    Hoje é 2026-10-07 (quarta-feira).\n");
    }

    [Fact]
    [Trait("AC", "012-010")]
    public void HelpMenu_IsTheSameAsTheFlask()
    {
        WhatsAppPrompts.HelpMenuText().ShouldBe(WhatsAppFixture.Section("help_menu").GetString());
    }
}

public class WebhookSignatureTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"texto\": \"olá  mundo\", \"numero_remetente\": \"5531999990001@s.whatsapp.net\"}");

    [Fact]
    [Trait("AC", "012-003")]
    public void Verify_AcceptsTheHexHmacOfTheRawBytes()
    {
        var signature = WebhookSignature.Compute(Body, "segredo-do-webhook");

        signature.Length.ShouldBe(64);
        signature.ShouldBe(signature.ToLowerInvariant());
        WebhookSignature.Verify(Body, signature, "segredo-do-webhook").ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-003")]
    public void Verify_RejectsOtherKeysBodiesAndEmptyValues()
    {
        var signature = WebhookSignature.Compute(Body, "segredo-do-webhook");

        WebhookSignature.Verify(Body, signature, "outra-chave").ShouldBeFalse();
        WebhookSignature.Verify(Encoding.UTF8.GetBytes("{}"), signature, "segredo-do-webhook").ShouldBeFalse();
        WebhookSignature.Verify(Body, signature.ToUpperInvariant(), "segredo-do-webhook").ShouldBeFalse();
        WebhookSignature.Verify(Body, "", "segredo-do-webhook").ShouldBeFalse();
        WebhookSignature.Verify(Body, null, "segredo-do-webhook").ShouldBeFalse();
        WebhookSignature.Verify(Body, signature, "").ShouldBeFalse();
        WebhookSignature.Verify(Body, "ç" + signature[1..], "segredo-do-webhook").ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "012-003")]
    public void Compute_MatchesAKnownHmacSha256Vector()
    {
        // HMAC_SHA256(key="key", "The quick brown fox jumps over the lazy dog") (RFC 4231 style vector).
        WebhookSignature.Compute(Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog"), "key")
            .ShouldBe("f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8");
    }
}

public class MessageIntentTests
{
    [Theory]
    [InlineData("sim", true, false)]
    [InlineData("Confirmar", true, false)]
    [InlineData("OK", true, false)]
    [InlineData("s", true, false)]
    [InlineData("não", false, true)]
    [InlineData("cancelar", false, true)]
    [InlineData("desistir", false, true)]
    [InlineData("n", false, true)]
    [InlineData("talvez", false, false)]
    [InlineData("sim senhor", false, false)]
    [Trait("AC", "012-014")]
    public void EventWords(string text, bool confirm, bool cancel)
    {
        var lower = MessageIntent.Lower(text);

        MessageIntent.IsEventConfirm(lower).ShouldBe(confirm);
        MessageIntent.IsEventCancel(lower).ShouldBe(cancel);
    }

    [Theory]
    [InlineData("sim", true)]
    [InlineData("não", true)]
    [InlineData("desistir", true)]
    [InlineData("ok", false)]
    [InlineData("cancelar", false)]
    [Trait("AC", "012-014")]
    public void ExclusiveEventWords_NeverReachTheTransactionReply(string text, bool exclusive) =>
        MessageIntent.IsExclusiveEventWord(MessageIntent.Lower(text)).ShouldBe(exclusive);

    [Theory]
    [InlineData("ok", true)]
    [InlineData("Confirmar", true)]
    [InlineData("quero trocar", true)]
    [InlineData("cancelar tudo", true)]
    [InlineData("comprei um book", true)]
    [InlineData("123", true)]
    [InlineData("confirma", false)]
    [InlineData("gastei 50", false)]
    [InlineData("12a", false)]
    [InlineData("", false)]
    [Trait("AC", "012-013")]
    public void LooksLikeTransactionReply_UsesSubstringsAndDigits(string text, bool expected) =>
        MessageIntent.LooksLikeTransactionReply(text.Trim().ToUpperInvariant()).ShouldBe(expected);

    [Theory]
    [InlineData("paguei a luz", true)]
    [InlineData("Quitei o cartão", true)]
    [InlineData("liquidei", true)]
    [InlineData("saldei a conta", true)]
    [InlineData("zerei a fatura", true)]
    [InlineData("gastei 50", false)]
    [Trait("AC", "012-206")]
    public void HasPaymentWord_FindsAnywhere(string text, bool expected) => MessageIntent.HasPaymentWord(text).ShouldBe(expected);

    [Theory]
    [InlineData("calcular rota", true)]
    [InlineData("quero CALCULAR a ROTA", true)]
    [InlineData("calcular", false)]
    [InlineData("rota", false)]
    [Trait("AC", "012-501")]
    public void WantsRoute_NeedsBothWords(string text, bool expected) => MessageIntent.WantsRoute(MessageIntent.Lower(text)).ShouldBe(expected);

    [Theory]
    [InlineData("cancelar", true)]
    [InlineData("  Cancela ", true)]
    [InlineData("cancelar tudo", false)]
    [InlineData("ok", false)]
    [Trait("AC", "012-012")]
    public void IsBareCancel_OnlyForTheSingleWord(string text, bool expected) =>
        MessageIntent.IsBareCancel(MessageIntent.Lower(text), text).ShouldBe(expected);

    [Theory]
    [InlineData("quanto devo?", true)]
    [InlineData("Criar evento", true)]
    [InlineData("horário livre", true)]
    [InlineData("1,2", false)]
    [InlineData("depois", false)]
    [Trait("AC", "012-015")]
    public void HasEscapeKeyword_IsASubstringTest(string text, bool expected) => MessageIntent.HasEscapeKeyword(text).ShouldBe(expected);

    [Fact]
    [Trait("AC", "012-015")]
    public void EscapeKeywords_AreTheThirtyOneOfTheFlask()
    {
        MessageIntent.EscapeKeywords.Count.ShouldBe(31);
    }

    [Theory]
    [InlineData("429 Quota exceeded", true)]
    [InlineData("Resource has been exhausted (e.g. check quota).", true)]
    [InlineData("Rate Limit atingido", true)]
    [InlineData("connection reset", false)]
    [InlineData(null, false)]
    [Trait("AC", "012-008")]
    public void QuotaMessage_MatchesTheFlaskTest(string? message, bool expected) => WhatsAppTexts.IsQuotaMessage(message).ShouldBe(expected);

    [Fact]
    [Trait("AC", "012-008")]
    public void AiLimitText_IsTheOneInTheContract()
    {
        WhatsAppTexts.AiLimit.ShouldStartWith("⚠️ *Limite de IA Excedido*\n\nO sistema atingiu");
        WhatsAppTexts.AiLimit.ShouldEndWith("⏰ O limite é renovado automaticamente.");
    }

    [Theory]
    [InlineData("casa", "Casa")]
    [InlineData("CASA", "Casa")]
    [InlineData("éLIA", "Élia")]
    [InlineData("", "")]
    [Trait("AC", "012-603")]
    public void Capitalize_LikePython(string input, string expected) => PyStrings.Capitalize(input).ShouldBe(expected);

    [Theory]
    [InlineData("notas fiscais", "Notas Fiscais")]
    [InlineData("o'neil 2nd", "O'Neil 2Nd")]
    [InlineData("IR 2026", "Ir 2026")]
    [Trait("AC", "012-801")]
    public void Title_LikePython(string input, string expected) => PyStrings.Title(input).ShouldBe(expected);
}
