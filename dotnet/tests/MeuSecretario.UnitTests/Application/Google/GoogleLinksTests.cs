using System.Text.Json;
using System.Web;
using MeuSecretario.Application.Google;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Application.Google;

internal static class GoogleFixtures
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "google-fixtures.json"))));

    public static JsonElement Root => Document.Value.RootElement;

    public static string Whatsapp(string name) => Root.GetProperty("whatsapp").GetProperty(name).GetString()!;
}

public class GoogleLinksTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    private GoogleLinks Links(string redirectUri = "https://h.exemplo/oauth2callback")
    {
        var settings = GoogleTestSupport.Settings(redirectUri: redirectUri);
        return new GoogleLinks(settings, new ConnectLinkTokenCodec(settings, _time));
    }

    [Fact]
    [Trait("AC", "010-88")]
    public void ConnectUrl_DropsTheLastSegmentAndAppendsRouteAndSignedToken()
    {
        var url = Links().ConnectUrl(7)!;

        url.ShouldStartWith("https://h.exemplo/connect-calendar/7?t=");
        var token = HttpUtility.ParseQueryString(new Uri(url).Query)["t"];
        new ConnectLinkTokenCodec(GoogleTestSupport.Settings(), _time).Verify(token).ShouldBe(7);
    }

    [Theory]
    [InlineData("https://h.exemplo/oauth2callback", "https://h.exemplo")]
    [InlineData("https://h.exemplo/a/b/oauth2callback", "https://h.exemplo/a/b")]
    [InlineData("semBarra", "semBarra")]
    [Trait("AC", "010-88")]
    public void Base_IsTheRedirectUriWithoutItsLastSegment(string redirectUri, string expected)
    {
        Links(redirectUri).Base().ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "010-88")]
    public void EmptyRedirectUri_MeansNoLink()
    {
        Links("").Base().ShouldBeNull();
        Links("").ConnectUrl(7).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-88")]
    public void TokenRevokedText_MatchesThePythonTemplates()
    {
        GoogleNotificationMessages.TokenRevoked("{connect_url}").ShouldBe(GoogleFixtures.Whatsapp("token_revoked_with_link"));
        GoogleNotificationMessages.TokenRevoked(null).ShouldBe(GoogleFixtures.Whatsapp("token_revoked_without_link"));
    }

    [Fact]
    [Trait("AC", "010-88")]
    public void ConnectedText_MatchesThePythonTemplate()
    {
        GoogleNotificationMessages.Connected("{nome}").ShouldBe(GoogleFixtures.Whatsapp("connected"));
    }
}
