using System.Security.Cryptography;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.UnitTests.Application.Whatsapp.Calendar;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Config;

/// <summary>
/// Desvios aprovados da 012 (2026-10-10): 152 e 191 (ramo do Gemini responde o status), 190 (desativar/desligar desligam) e 153
/// (API key decifrada). Os cenários antigos do Flask seguem no JSON de paridade como evidência.
/// </summary>
public class NotificationDeviationTests
{
    private static CalendarRig NewRig() => new("2026-10-08T14:20:00-03:00");

    private static async Task<(string Reply, MemoryConfigStore Store)> Shortcut(string message, WhatsAppNotificationConfig? initial = null)
    {
        var rig = NewRig();
        var store = new MemoryConfigStore { Notification = initial };
        var reply = await new NotificationsIntent(rig.Llm, store).HandleAsync(rig.Context(message), CancellationToken.None);
        return (reply, store);
    }

    private static async Task<(string Reply, MemoryConfigStore Store)> Gemini(string json, bool failUpdate = false)
    {
        var rig = NewRig();
        var store = new MemoryConfigStore { FailNotificationUpdate = failUpdate };
        rig.Llm.Then(json);
        var reply = await new NotificationsIntent(rig.Llm, store).HandleAsync(rig.Context("configurar notificação"), CancellationToken.None);
        return (reply, store);
    }

    [Theory]
    [Trait("AC", "012-190")]
    [InlineData("desativar resumo matinal")]
    [InlineData("desligar o resumo")]
    [InlineData("desative o briefing")]
    public async Task Morning_DisableWords_TurnItOff(string message)
    {
        var (reply, store) = await Shortcut(message);

        reply.ShouldStartWith("✅ Resumo matinal desativado");
        reply.ShouldContain("• Ativo: Não");
        store.Notification!.MorningActive.ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "012-190")]
    [InlineData("desligar o check-in noturno")]
    [InlineData("desativar checkin")]
    [InlineData("check-in off")]
    public async Task Checkin_DisableWords_TurnItOff(string message)
    {
        var (reply, store) = await Shortcut(message);

        reply.ShouldStartWith("✅ Check-in noturno desativado");
        store.Notification!.CheckinActive.ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "012-190")]
    [InlineData("ativar resumo matinal às 8h", true)]
    [InlineData("ligar o resumo matinal 7:30", true)]
    [InlineData("ativar check-in noturno às 21h", false)]
    public async Task EnableWords_StillTurnItOn(string message, bool morning)
    {
        var initial = new WhatsAppNotificationConfig(false, new TimeOnly(7, 0), false, new TimeOnly(20, 0), true);
        var (_, store) = await Shortcut(message, initial);

        (morning ? store.Notification!.MorningActive : store.Notification!.CheckinActive).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-152")]
    public async Task Gemini_DailyAgenda_SavesAndAnswersTheStatus()
    {
        var (reply, store) = await Gemini("""{"tipo": "agenda_diaria", "acao": "configurar", "hora": "08:30"}""");

        reply.ShouldBe("✅ Resumo matinal ativado e horário configurado para 08:30\n\n📱 *Agenda Diária - Status atual:*\n• Ativa: Sim\n• Horário: 08:30\n");
        store.Notification!.MorningActive.ShouldBeTrue();
        store.Notification.MorningTime.ShouldBe(new TimeOnly(8, 30));
    }

    [Fact]
    [Trait("AC", "012-152")]
    public async Task Gemini_DailyAgenda_Disable_AnswersNo()
    {
        var (reply, store) = await Gemini("""{"tipo": "agenda_diaria", "acao": "desativar"}""");

        reply.ShouldContain("• Ativa: Não");
        store.Notification!.MorningActive.ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "012-191")]
    [InlineData("ativar", true)]
    [InlineData("desativar", false)]
    [InlineData("configurar", true)]
    public async Task Gemini_BillsDue_WritesFinancialAlertsAndAnswersTheStatus(string action, bool expected)
    {
        var (reply, store) = await Gemini($$"""{"tipo": "contas_vencer", "acao": "{{action}}", "hora": "09:00", "dias_antes": 2}""");

        store.Notification!.FinancialAlertsActive.ShouldBe(expected);
        store.Writes.ShouldBe([$"alerts {expected}"]);
        reply.ShouldBe(
            $"✅ Alertas financeiros {(expected ? "ativados" : "desativados")} com sucesso\n\n" +
            $"📱 *Contas a Vencer - Status atual:*\n• Ativa: {(expected ? "Sim" : "Não")}\n• Horário: junto com o resumo matinal (07:00)\n");
    }

    [Fact]
    [Trait("AC", "012-191")]
    public async Task Gemini_BillsDue_UnknownAction_DoesNotWrite()
    {
        var (reply, store) = await Gemini("""{"tipo": "contas_vencer", "acao": "x"}""");

        reply.ShouldBe("❌ Ação não reconhecida");
        store.Writes.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-191")]
    public async Task Gemini_BillsDue_WhenTheWriteFails_AnswersWithoutTheReason()
    {
        var (reply, _) = await Gemini("""{"tipo": "contas_vencer", "acao": "ativar"}""", failUpdate: true);

        reply.ShouldBe("❌ Erro ao atualizar configuração.");
        reply.ShouldNotContain("segredo");
    }

    // ---------------------------------------------------------------- 153

    private static IEncryptionService Encryption()
    {
        var encryption = Substitute.For<IEncryptionService>();
        encryption.LooksEncrypted(Arg.Any<string>()).Returns(call => call.Arg<string>().StartsWith("gAAAAA", StringComparison.Ordinal));
        encryption.Decrypt("gAAAAAcifrada").Returns("chave-em-texto-puro");
        encryption.Decrypt("gAAAAAquebrada").Returns<string>(_ => throw new CryptographicException("token inválido"));
        return encryption;
    }

    private static async Task<string> ApiKey(string? stored)
    {
        var store = new MemoryConfigStore { User = (stored, "Ana") };
        var rig = NewRig();
        return await new ApiKeyIntent(store, Encryption()).HandleAsync(rig.Context(), CancellationToken.None);
    }

    [Fact]
    [Trait("AC", "012-153")]
    public async Task ApiKey_Encrypted_IsShownDecrypted()
    {
        var reply = await ApiKey("gAAAAAcifrada");

        reply.ShouldContain("`chave-em-texto-puro`");
        reply.ShouldNotContain("gAAAAA");
    }

    [Fact]
    [Trait("AC", "012-153")]
    public async Task ApiKey_LegacyPlainText_IsShownAsIs() => (await ApiKey("chave-legada")).ShouldContain("`chave-legada`");

    [Fact]
    [Trait("AC", "012-153")]
    public async Task ApiKey_UnreadableToken_IsNeverShown()
    {
        var reply = await ApiKey("gAAAAAquebrada");

        reply.ShouldBe(MeuSecretario.Domain.Whatsapp.ApiKeyMessage.NotFound);
    }
}
