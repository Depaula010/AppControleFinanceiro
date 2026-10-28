using MeuSecretario.Api.Whatsapp;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.UnitTests.Api;

/// <summary>Desvio 266: o aviso ao dono diz a causa certa e nunca carrega dado do usuário.</summary>
public class WebhookFailureAlertTests
{
    [Fact]
    public void Quota_IsItsOwnSource_AndPointsToTheBalance()
    {
        var (source, message) = WebhookFailureAlert.Describe(new LlmQuotaExceededException("429 Quota exceeded - Gemini API rate limit reached"));

        source.ShouldBe(WebhookFailureAlert.QuotaSource);
        message.ShouldContain("saldo");
        message.ShouldContain("menu");
    }

    [Fact]
    public void PermissionDenied_PointsToTheKeys()
    {
        var (source, message) = WebhookFailureAlert.Describe(new LlmPermissionDeniedException("403"));

        source.ShouldBe(WebhookFailureAlert.PermissionSource);
        message.ShouldContain("usar_chave_propria");
    }

    [Fact]
    public void GenericGeminiFailure_ShowsTheSafeMessage_AndHowToChangeTheModel()
    {
        var (source, message) = WebhookFailureAlert.Describe(new LlmException("Falha na API do Gemini (HTTP 404)."));

        source.ShouldBe(WebhookFailureAlert.ModelSource);
        message.ShouldContain("HTTP 404");
        message.ShouldContain("gemini.modelo");
    }

    [Fact]
    public void QuotaTextInAnotherException_StillCountsAsQuota()
    {
        WebhookFailureAlert.Describe(new InvalidOperationException("429 Too Many Requests")).Source.ShouldBe(WebhookFailureAlert.QuotaSource);
    }

    [Fact]
    public void AnythingElse_ShowsOnlyTheExceptionType()
    {
        var (source, message) = WebhookFailureAlert.Describe(new InvalidOperationException("senha=segredo, texto do usuario: gastei 21 reais"));

        source.ShouldBe(WebhookFailureAlert.OtherSource);
        message.ShouldContain("InvalidOperationException");
        message.ShouldNotContain("segredo");
        message.ShouldNotContain("gastei");
    }
}
