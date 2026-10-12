using MeuSecretario.Application.Abstractions;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Google;

internal static class GoogleTestSupport
{
    public const string SecretKey = "0123456789abcdef0123456789abcdef";

    public static IIntegrationSettings Settings(
        string secretKey = SecretKey,
        string redirectUri = "https://h.exemplo/oauth2callback",
        string clientId = "client-id-teste",
        string clientSecret = "client-secret-teste",
        DateTimeOffset? legacyUntil = null)
    {
        var settings = Substitute.For<IIntegrationSettings>();
        settings.ApiSecretKey.Returns(secretKey);
        settings.GoogleRedirectUri.Returns(redirectUri);
        settings.GoogleClientId.Returns(clientId);
        settings.GoogleClientSecret.Returns(clientSecret);
        settings.GoogleConnectLegacyUntil.Returns(legacyUntil);
        return settings;
    }
}
