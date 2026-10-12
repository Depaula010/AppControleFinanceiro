using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.UnitTests.Application.ApiKeys;

/// <summary>Contrato da porta <see cref="IApiKeyProvider"/> (spec 008 §5): mensagens literais e segredo fora do ToString.</summary>
public class ApiKeyContractTests
{
    [Theory]
    [Trait("AC", "008-12")]
    [InlineData("gemini", true)]
    [InlineData("weather", true)]
    [InlineData("openroute", true)]
    [InlineData("Gemini", false)]
    [InlineData("openai", false)]
    [InlineData("", false)]
    [InlineData(" gemini", false)]
    [InlineData(null, false)]
    public void ApiProviders_IsValid_IsExactAndCaseSensitive(string? provider, bool expected)
    {
        ApiProviders.IsValid(provider).ShouldBe(expected);
    }

    [Fact]
    public void ApiProviders_ListText_MatchesTheFlaskMessage()
    {
        ApiProviders.ValidListText.ShouldBe(string.Join(", ", ApiProviders.All));
        ApiProviders.ValidListText.ShouldBe("gemini, weather, openroute");
    }

    [Fact]
    public void KeyTypes_AreTheDatabaseValues()
    {
        KeyTypes.Own.ShouldBe("propria");
        KeyTypes.System.ShouldBe("sistema");
    }

    [Fact]
    [Trait("AC", "008-60")]
    public void InvalidProvider_Message() =>
        ApiKeyMessages.InvalidProvider("openai").ShouldBe("Provedor inválido: openai");

    [Fact]
    [Trait("AC", "008-61")]
    public void NoPreference_Message() =>
        ApiKeyMessages.NoPreference("gemini").ShouldBe(
            "⚙️ Você ainda não configurou suas preferências para gemini.\n" +
            "Acesse o dashboard e escolha:\n" +
            "• Usar sua própria chave (grátis)\n" +
            "• Usar chave do sistema (com custo adicional)");

    [Fact]
    [Trait("AC", "008-62")]
    public void OwnKeyMissing_Message() =>
        ApiKeyMessages.OwnKeyMissing("weather").ShouldBe(
            "🔑 Você configurou para usar sua chave de weather, mas ainda não cadastrou.\n" +
            "Acesse o dashboard para cadastrar sua chave.\n" +
            "Ou responda: *Cadastrar chave de weather*");

    [Fact]
    [Trait("AC", "008-64")]
    public void SystemKeyUnavailable_Message() =>
        ApiKeyMessages.SystemKeyUnavailable("openroute").ShouldBe(
            "❌ Chave do sistema para openroute indisponível no momento.\n" +
            "Entre em contato com o suporte ou configure sua própria chave.");

    [Fact]
    [Trait("AC", "008-66")]
    public void LimitReached_Message() =>
        ApiKeyMessages.LimitReached("gemini", "Prata", 500, 501).ShouldBe(
            "🚫 Limite mensal atingido para gemini!\n" +
            "Seu plano Prata permite 500 chamadas/mês.\n" +
            "Você já usou 501 chamadas.\n\n" +
            "💡 Opções:\n" +
            "• Faça upgrade do plano\n" +
            "• Use sua própria chave (sem limites)");

    [Fact]
    [Trait("AC", "008-68")]
    public void Resolved_ToString_NeverContainsTheKey()
    {
        var resolved = new ApiKeyResolution.Resolved("segredo-super-secreto", KeyTypes.Own);

        resolved.ToString().ShouldNotContain("segredo");
        resolved.ToString().ShouldContain("propria");
        resolved.Key.ShouldBe("segredo-super-secreto");
        ApiKeyResolution result = resolved;
        $"{result}".ShouldNotContain("segredo");
    }

    [Fact]
    public void Failed_CarriesReasonAndLiteralMessage()
    {
        var failed = new ApiKeyResolution.Failed(ApiKeyFailure.NoPreference, ApiKeyMessages.NoPreference("gemini"));

        failed.Reason.ShouldBe(ApiKeyFailure.NoPreference);
        failed.Message.ShouldStartWith("⚙️");
    }
}
