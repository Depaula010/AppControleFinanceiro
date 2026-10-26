using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>Desvio 265: "menu" e "ajuda" respondem sem chamar o Gemini (que pode estar sem cota ou saldo).</summary>
public class HelpCommandStepTests
{
    private static PipelineContext Context(string message) =>
        new(new WhatsAppUser(7, "Rafael"), "5531999990001", message, message.ToLowerInvariant(), null);

    [Fact]
    public async Task Menu_AnswersTheFixedHelpText()
    {
        var reply = await new HelpCommandStep().HandleAsync(Context("menu"), CancellationToken.None);

        reply.ShouldBe(WhatsAppPrompts.HelpMenuText());
    }

    [Fact]
    public async Task AnythingElse_PassesThrough()
    {
        (await new HelpCommandStep().HandleAsync(Context("gastei 21 reais na padaria"), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void RunsAfterTheCheckinAndBeforeTheTransactionReply()
    {
        new HelpCommandStep().Order.ShouldBeGreaterThan(StepOrder.Checkin);
        new HelpCommandStep().Order.ShouldBeLessThan(StepOrder.TransactionReply);
    }

    [Fact]
    public async Task InThePipeline_MenuNeverCallsTheGemini()
    {
        var llm = Substitute.For<ILlmClient>();
        llm.GenerateTextAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>()).Returns<Task<string>>(_ => throw new LlmException("429 Quota exceeded"));
        var users = Substitute.For<IWhatsAppUserLookup>();
        users.FindByNumberAsync("5531999990001", Arg.Any<CancellationToken>()).Returns(new WhatsAppUser(7, "Rafael"));
        var pipeline = new HandleWhatsAppMessage(
            users, [new HelpCommandStep()], new IntentClassifier(llm), new IntentRouter([new HelpIntent()], Substitute.For<IWhatsAppDiagnostics>()));

        var reply = await pipeline.ExecuteAsync(new WebhookInput("Menu", "5531999990001", null), CancellationToken.None);

        reply.ShouldBe(WhatsAppPrompts.HelpMenuText());
        await llm.DidNotReceiveWithAnyArgs().GenerateTextAsync(default!, default);
    }
}
