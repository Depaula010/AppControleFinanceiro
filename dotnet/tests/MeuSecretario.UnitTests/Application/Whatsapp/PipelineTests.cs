using System.Text.Json.Nodes;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Llm;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

public class IntentRouterTests
{
    [Fact]
    [Trait("AC", "012-009")]
    public async Task TemplateIntent_SwallowsTheException_AndAnswersTheGenericText()
    {
        var diagnostics = new NullDiagnostics();
        var router = new IntentRouter([new ThrowingIntent("Consulta Saldo", new InvalidOperationException("429 quota"), swallows: true)], diagnostics);

        var reply = await router.DispatchAsync("Consulta Saldo", Context(), CancellationToken.None);

        reply.ShouldBe("❌ Ops! Algo deu errado. Por favor, tente novamente.");
        diagnostics.Failures.ShouldBe([("Consulta Saldo", "InvalidOperationException")]);
    }

    [Fact]
    [Trait("AC", "012-009")]
    public async Task InlineIntent_LetsTheExceptionRise()
    {
        var router = new IntentRouter([new ThrowingIntent("Consulta Potes", new LlmQuotaExceededException("429 Quota exceeded"), swallows: false)], new NullDiagnostics());

        var act = () => router.DispatchAsync("Consulta Potes", Context(), CancellationToken.None);

        (await Should.ThrowAsync<LlmQuotaExceededException>(act)).Message.ShouldContain("429");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Quitar Conta Fixa")]
    [InlineData("consulta saldo")]
    [InlineData("Consulta Periodo")]
    [Trait("AC", "012-011")]
    public async Task UnknownNames_AnswerNotUnderstood(string? name)
    {
        var router = new IntentRouter([new FixedIntent("Consulta Período", "ok")], new NullDiagnostics());

        (await router.DispatchAsync(name, Context(), CancellationToken.None)).ShouldBe("🤔 Não entendi. Tente 'gastei 50' ou 'o que você pode fazer?'.");
    }

    [Fact]
    [Trait("AC", "012-011")]
    public async Task ExactAccentedNames_Dispatch()
    {
        var router = new IntentRouter([new FixedIntent("Consulta Período", "periodo"), new FixedIntent("Vencimentos Amanhã", "amanha")], new NullDiagnostics());

        (await router.DispatchAsync("Consulta Período", Context(), CancellationToken.None)).ShouldBe("periodo");
        (await router.DispatchAsync("Vencimentos Amanhã", Context(), CancellationToken.None)).ShouldBe("amanha");
    }

    [Fact]
    public void DuplicateNames_FailAtConstruction() =>
        Should.Throw<ArgumentException>(() => new IntentRouter([new FixedIntent("A", "1"), new FixedIntent("A", "2")], new NullDiagnostics()));

    private static IntentContext Context() => new(new WhatsAppUser(7, "Ana"), WhatsAppRig.Number, "oi", null);
}

public class IntentClassifierTests
{
    [Fact]
    [Trait("AC", "012-016")]
    public async Task Classify_SendsTheRenderedPrompt_WithTheUserAndTheIntentCachePolicy()
    {
        var llm = new ScriptedLlm().Then("```json\n{\"intent\": \"Consulta Saldo\"}\n```");

        var intent = await new IntentClassifier(llm).ClassifyAsync("qual meu saldo?", 7, CancellationToken.None);

        intent.ShouldBe("Consulta Saldo");
        var request = llm.Requests.Single();
        request.Prompt.ShouldBe(WhatsAppPrompts.Intent("qual meu saldo?"));
        request.UserId.ShouldBe(7);
        request.Cache.ShouldBeSameAs(LlmCachePolicies.Intent);
        request.Cache!.Prefix.ShouldBe("intent");
        request.Cache.Ttl.ShouldBe(TimeSpan.FromDays(7));
        request.Cache.UserSpecific.ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"intent": null}""")]
    [InlineData("""{"outra": 1}""")]
    [InlineData("""{"intent": 5}""")]
    public async Task Classify_WithoutATextIntent_ReturnsNull(string response) =>
        (await new IntentClassifier(new ScriptedLlm().Then(response)).ClassifyAsync("x", 7, CancellationToken.None)).ShouldBeNull();

    [Fact]
    public async Task Classify_EmptyResponse_Throws() =>
        await Should.ThrowAsync<LlmException>(() => new IntentClassifier(new ScriptedLlm().Then("")).ClassifyAsync("x", 7, CancellationToken.None));

    [Fact]
    public async Task Classify_ArrayOrInvalidJson_Throws()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => new IntentClassifier(new ScriptedLlm().Then("[1]")).ClassifyAsync("x", 7, CancellationToken.None));
        await Should.ThrowAsync<System.Text.Json.JsonException>(() => new IntentClassifier(new ScriptedLlm().Then("não é json")).ClassifyAsync("x", 7, CancellationToken.None));
    }
}

public class HandleWhatsAppMessageTests
{
    [Fact]
    [Trait("AC", "012-006")]
    public async Task Number_IsCutAtTheAt_AndSanitized()
    {
        var rig = new WhatsAppRig();
        rig.Llm.Then("""{"intent": null}""");

        await rig.SendAsync("ajuda", "55<31>9;99|9&0001@s.whatsapp.net");

        rig.Calls[0].ShouldBe("user_service.check_user_exists:553199990001");
    }

    [Fact]
    [Trait("AC", "012-006")]
    public async Task Text_IsTrimmedCollapsedAndCutAt1000_KeepingSpecialCharacters()
    {
        var rig = new WhatsAppRig();
        rig.Llm.Then("""{"intent": "Menu de Ajuda"}""");

        await rig.SendAsync("  gastei   {50}  <ok> $x  " + new string('a', 1200));

        var prompt = rig.Llm.Requests.Single().Prompt;
        prompt.ShouldContain("\"gastei {50} <ok> $x aaa");
        prompt.ShouldNotContain(new string('a', 1000));
    }

    [Fact]
    [Trait("AC", "012-007")]
    public async Task UnknownNumber_AnswersTheRegistrationText_WithoutCallingGemini()
    {
        var rig = new WhatsAppRig(userExists: false);

        var reply = await rig.SendAsync("oi");

        reply.ShouldBe(WhatsAppTexts.NotRegistered);
        rig.Llm.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-010")]
    public async Task HelpMenu_AnswersTheFixedMenu()
    {
        var rig = new WhatsAppRig();
        rig.Llm.Then("""{"intent": "Menu de Ajuda"}""");

        (await rig.SendAsync("o que voce faz?")).ShouldBe(WhatsAppPrompts.HelpMenuText());
    }

    [Fact]
    [Trait("AC", "012-008")]
    public async Task GeminiFailure_RisesForTheEndpointToMap()
    {
        var rig = new WhatsAppRig();
        rig.Llm.ThenThrow(new LlmQuotaExceededException("429 Quota exceeded - Gemini API rate limit reached"));

        var thrown = await Should.ThrowAsync<LlmQuotaExceededException>(() => rig.SendAsync("oi"));

        WhatsAppTexts.IsQuotaMessage(thrown.Message).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-017")]
    public async Task Steps_RunInOrder_AndTheFirstReplyWins()
    {
        var order = new List<int>();
        var users = new FakeUsers(true, []);
        var llm = new ScriptedLlm();
        var steps = new List<IWhatsAppStep> { new LambdaStep(70, order, "b"), new LambdaStep(30, order, null), new LambdaStep(50, order, "c") };
        var useCase = new HandleWhatsAppMessage(users, steps, new IntentClassifier(llm), new IntentRouter([], new NullDiagnostics()));

        var reply = await useCase.ExecuteAsync(new WebhookInput("oi", "5531@x", null), CancellationToken.None);

        reply.ShouldBe("c");
        order.ShouldBe([30, 50]);
        llm.Requests.ShouldBeEmpty();
    }

    private sealed class LambdaStep(int order, List<int> log, string? reply) : IWhatsAppStep
    {
        public int Order => order;

        public Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
        {
            log.Add(order);
            return Task.FromResult(reply);
        }
    }
}

public class CoreStepsTests
{
    [Fact]
    [Trait("AC", "012-012")]
    public async Task BareCancel_WithoutAnythingPending_Warns()
    {
        var rig = new WhatsAppRig();

        (await rig.SendAsync("cancelar")).ShouldBe(WhatsAppTexts.NothingPendingToCancel);
        rig.Llm.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-012")]
    public async Task BareCancel_WithAPendingTransaction_GoesOnToGemini()
    {
        var rig = new WhatsAppRig();
        await rig.SeedAsync("last_pending:" + WhatsAppRig.Number, JsonValue.Create("abc12345"));
        rig.Llm.Then("""{"intent": "Menu de Ajuda"}""");

        (await rig.SendAsync("cancela")).ShouldBe(WhatsAppPrompts.HelpMenuText());
    }

    [Fact]
    [Trait("AC", "012-012")]
    public async Task BareCancel_WithAPendingEvent_GoesOnToGemini()
    {
        var rig = new WhatsAppRig();
        await rig.Events.CreateAsync(WhatsAppRig.Number, new PendingEvent(7, "Academia", "2026-10-08", "07:00", null, null, null), CancellationToken.None);
        rig.Llm.Then("""{"intent": "Menu de Ajuda"}""");

        (await rig.SendAsync("cancela")).ShouldBe(WhatsAppPrompts.HelpMenuText());
    }

    [Fact]
    [Trait("AC", "012-015")]
    public async Task ActiveCheckin_WithAnEscapeWord_ClearsTheSession_AndContinues()
    {
        var rig = new WhatsAppRig();
        await rig.SeedAsync("nightly_checkin_active:" + WhatsAppRig.Number, JsonValue.Create("chk00001"));
        rig.Llm.Then("""{"intent": "Menu de Ajuda"}""");

        (await rig.SendAsync("quanto devo?")).ShouldBe(WhatsAppPrompts.HelpMenuText());
        (await rig.Cache.ExistsAsync("nightly_checkin_active:" + WhatsAppRig.Number)).ShouldBeFalse();
        rig.Calls.ShouldNotContain("NightlyCheckinService.process_response");
    }

    [Fact]
    [Trait("AC", "012-015")]
    public async Task ActiveCheckin_WithoutAnEscapeWord_GoesToTheResponder()
    {
        var rig = new WhatsAppRig(checkinReply: "PAGO");
        await rig.SeedAsync("nightly_checkin_active:" + WhatsAppRig.Number, JsonValue.Create("chk00001"));

        (await rig.SendAsync("1, 3")).ShouldBe("PAGO");
        rig.Llm.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-015")]
    public async Task ActiveCheckin_WithTheDefaultResponder_FallsThroughToGemini()
    {
        var cache = new TestSupport.InMemoryCache();
        await cache.SetJsonAsync("nightly_checkin_active:" + WhatsAppRig.Number, "chk00001", TimeSpan.FromHours(1));
        var step = new CheckinStep(new MeuSecretario.Infrastructure.Whatsapp.CheckinSessionState(cache), new NullNightlyCheckinResponder());

        var reply = await step.HandleAsync(
            new PipelineContext(new WhatsAppUser(7, "Ana"), WhatsAppRig.Number, "1, 3", "1, 3", null), CancellationToken.None);

        reply.ShouldBeNull();
        (await cache.ExistsAsync("nightly_checkin_active:" + WhatsAppRig.Number)).ShouldBeTrue();
    }
}

public class PendingStoresTests
{
    [Fact]
    [Trait("AC", "012-018")]
    public async Task PendingTransaction_RoundTripsKeepingUnknownKeys_AndTheTtl()
    {
        var rig = new WhatsAppRig();
        var data = JsonNode.Parse("""{"usuario_id": 7, "descricao": "Pão Quente", "valor_db": -50.0, "extra": {"k": [1, "ç"]}}""")!.AsObject();

        await rig.TransactionState.SaveAsync(WhatsAppRig.Number, "abc12345", data, CancellationToken.None);
        var read = await rig.TransactionState.GetAsync(WhatsAppRig.Number, "abc12345", CancellationToken.None);

        JsonNode.DeepEquals(read, data).ShouldBeTrue();
        read!["valor_db"]!.ToJsonString().ShouldBe("-50.0");
        rig.Cache.TtlOf("pending_tx:" + WhatsAppRig.Number + ":abc12345").ShouldBe(TimeSpan.FromSeconds(300));
    }

    [Fact]
    [Trait("AC", "012-018")]
    public async Task DeleteBoth_RemovesThePendingAndLastPending()
    {
        var rig = new WhatsAppRig();
        await rig.TransactionState.SaveAsync(WhatsAppRig.Number, "abc12345", new JsonObject(), CancellationToken.None);
        await rig.SeedAsync("last_pending:" + WhatsAppRig.Number, JsonValue.Create("abc12345"));

        (await rig.TransactionState.GetLastPendingIdAsync(WhatsAppRig.Number, CancellationToken.None)).ShouldBe("abc12345");
        await rig.TransactionState.DeleteBothAsync(WhatsAppRig.Number, "abc12345", CancellationToken.None);

        (await rig.Cache.KeysByPatternAsync("*")).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-018")]
    public async Task PendingEvent_IsStoredWithThePythonKeys_AndTheLatestIsTheLastKey()
    {
        var rig = new WhatsAppRig();
        var id = await rig.Events.CreateAsync(WhatsAppRig.Number, new PendingEvent(7, "Academia", "2026-10-08", "07:00", null, null, "Rua X"), CancellationToken.None);

        id.ShouldBe("ab12cd34");
        rig.Cache.RawValue("pending_event:" + WhatsAppRig.Number + ":ab12cd34").ShouldBe(
            """{"usuario_id":7,"titulo":"Academia","data_evento":"2026-10-08","hora_inicio":"07:00","hora_fim":null,"descricao":null,"localizacao":"Rua X"}""");
        rig.Cache.TtlOf("pending_event:" + WhatsAppRig.Number + ":ab12cd34").ShouldBe(TimeSpan.FromSeconds(300));

        var latest = await rig.Events.GetLatestAsync(WhatsAppRig.Number, CancellationToken.None);
        latest.Id.ShouldBe("ab12cd34");
        latest.Data!.Title.ShouldBe("Academia");

        (await rig.Events.GetLatestAsync("5599", CancellationToken.None)).ShouldBe(new LatestPendingEvent(null, null));
    }
}
