using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Integrations;
using MeuSecretario.Domain.ApiKeys;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Integrations;

/// <summary>RN-010-66 / AC-010-123.</summary>
public class ProviderKeySelectorTests
{
    private readonly IApiKeyProvider _apiKeys = Substitute.For<IApiKeyProvider>();
    private readonly IIntegrationSettings _settings = Substitute.For<IIntegrationSettings>();
    private readonly ProviderKeySelector _selector;

    public ProviderKeySelectorTests()
    {
        _settings.GeminiApiKey.Returns("sys-gemini");
        _settings.WeatherApiKey.Returns("sys-weather");
        _settings.OpenRouteApiKey.Returns(string.Empty);
        _selector = new ProviderKeySelector(_apiKeys, _settings);
    }

    [Theory]
    [Trait("AC", "010-123")]
    [InlineData(ApiProviders.Gemini, "sys-gemini")]
    [InlineData(ApiProviders.Weather, "sys-weather")]
    public async Task NullUser_UsesTheSystemKeyWithoutCallingTheApiKeyProvider(string provider, string expected)
    {
        var result = await _selector.SelectAsync(null, provider, CancellationToken.None);

        result.Key.ShouldBe(expected);
        result.Failure.ShouldBeNull();
        await _apiKeys.DidNotReceiveWithAnyArgs().ResolveAsync(default, default!, default);
    }

    [Fact]
    [Trait("AC", "010-123")]
    public async Task NullUser_WithEmptySystemKey_MeansNoKey()
    {
        var result = await _selector.SelectAsync(null, ApiProviders.OpenRoute, CancellationToken.None);

        result.HasKey.ShouldBeFalse();
        result.Failure.ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "010-123")]
    [InlineData(KeyTypes.Own)]
    [InlineData(KeyTypes.System)]
    public async Task User_WithResolvedKey_ReturnsTheResolvedKey(string keyType)
    {
        _apiKeys.ResolveAsync(5, ApiProviders.Gemini, Arg.Any<CancellationToken>())
            .Returns(new ApiKeyResolution.Resolved("user-key", keyType));

        var result = await _selector.SelectAsync(5, ApiProviders.Gemini, CancellationToken.None);

        result.Key.ShouldBe("user-key");
        result.Failure.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-123")]
    public async Task User_WithFailedResolution_ReturnsTheLiteralMessage()
    {
        var message = ApiKeyMessages.NoPreference(ApiProviders.Gemini);
        _apiKeys.ResolveAsync(5, ApiProviders.Gemini, Arg.Any<CancellationToken>())
            .Returns(new ApiKeyResolution.Failed(ApiKeyFailure.NoPreference, message));

        var result = await _selector.SelectAsync(5, ApiProviders.Gemini, CancellationToken.None);

        result.Key.ShouldBeNull();
        result.Failure.ShouldNotBeNull();
        result.Failure.Reason.ShouldBe(ApiKeyFailure.NoPreference);
        result.Failure.Message.ShouldBe(message);
    }

    [Theory]
    [Trait("AC", "010-123")]
    [InlineData("openai")]
    [InlineData("Gemini")]
    public async Task UnknownProvider_Throws(string provider)
    {
        await Should.ThrowAsync<ArgumentException>(() => _selector.SelectAsync(null, provider, CancellationToken.None));
        await Should.ThrowAsync<ArgumentException>(() => _selector.SelectAsync(5, provider, CancellationToken.None));
    }

    [Fact]
    [Trait("AC", "010-123")]
    public void ToString_NeverContainsTheKey()
    {
        ProviderKeyResult.WithKey("super-secret").ToString().ShouldNotContain("super-secret");
        new GoogleTokenSet("tok-secret", "ref-secret", null, []).ToString().ShouldNotContain("secret");
        new GoogleTokenRecord("tok-secret", "ref-secret", null, null, false).ToString().ShouldNotContain("secret");
        new GoogleCredentialResult(GoogleCredentialStatus.Connected, "tok-secret", []).ToString().ShouldNotContain("secret");
    }
}
