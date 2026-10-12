using System.Net;
using System.Web;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Google;
using MeuSecretario.UnitTests.Application.Google;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Infrastructure.Google;

public sealed class GoogleOAuthClientTests : IDisposable
{
    private readonly FakeHttpHandler _handler = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

    public void Dispose() => _handler.Dispose();

    private GoogleOAuthClient Client(string clientId = "client-id-teste", string clientSecret = "client-secret-teste") =>
        new(
            GoogleTestSupport.Settings(redirectUri: "https://h.exemplo/oauth2callback", clientId: clientId, clientSecret: clientSecret),
            new FakeGoogleHttpClientFactory(_handler),
            _time);

    private static Dictionary<string, string> Form(RecordedRequest request) =>
        HttpUtility.ParseQueryString(request.BodyText!).AllKeys.ToDictionary(k => k!, k => HttpUtility.ParseQueryString(request.BodyText!)[k]!);

    [Fact]
    [Trait("AC", "010-72")]
    public void BuildAuthorizationUrl_HasEveryParameter_AndNoPkce()
    {
        var url = Client().BuildAuthorizationUrl("o.state");

        url.ShouldStartWith("https://accounts.google.com/o/oauth2/auth?");
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        query["client_id"].ShouldBe("client-id-teste");
        query["redirect_uri"].ShouldBe("https://h.exemplo/oauth2callback");
        query["response_type"].ShouldBe("code");
        query["scope"].ShouldBe("https://www.googleapis.com/auth/calendar https://www.googleapis.com/auth/drive.file");
        query["state"].ShouldBe("o.state");
        query["access_type"].ShouldBe("offline");
        query["include_granted_scopes"].ShouldBe("true");
        query["prompt"].ShouldBe("consent");
        url.ShouldNotContain("code_challenge");
        url.ShouldNotContain("client-secret-teste");
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("id", "")]
    [Trait("AC", "010-74")]
    public async Task WithoutClientCredentials_ThrowsConfigurationError_WithoutTouchingTheNetwork(string id, string secret)
    {
        var client = Client(id, secret);

        Should.Throw<GoogleOAuthConfigurationException>(() => client.BuildAuthorizationUrl("s"));
        await Should.ThrowAsync<GoogleOAuthConfigurationException>(() => client.ExchangeCodeAsync("c", CancellationToken.None));
        await Should.ThrowAsync<GoogleOAuthConfigurationException>(() => client.RefreshAsync("r", CancellationToken.None));
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-77")]
    public async Task ExchangeCode_PostsTheFormAndParsesTheResponse()
    {
        _handler.Enqueue(
            HttpStatusCode.OK,
            """{"access_token":"acc","refresh_token":"ref","expires_in":3599,"scope":"https://www.googleapis.com/auth/calendar https://www.googleapis.com/auth/drive.file","token_type":"Bearer"}""");

        var tokens = await Client().ExchangeCodeAsync("codigo-123", CancellationToken.None);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ToString().ShouldBe("https://oauth2.googleapis.com/token");
        request.Headers["Content-Type"].ShouldStartWith("application/x-www-form-urlencoded");
        Form(request).ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = "codigo-123",
            ["redirect_uri"] = "https://h.exemplo/oauth2callback",
            ["client_id"] = "client-id-teste",
            ["client_secret"] = "client-secret-teste",
        });
        request.BodyText!.ShouldNotContain("code_verifier");

        tokens.AccessToken.ShouldBe("acc");
        tokens.RefreshToken.ShouldBe("ref");
        tokens.ExpiresAtUtc.ShouldBe(_time.GetUtcNow().AddSeconds(3599));
        tokens.Scopes.ShouldBe(["https://www.googleapis.com/auth/calendar", "https://www.googleapis.com/auth/drive.file"]);
    }

    [Fact]
    [Trait("AC", "010-78")]
    public async Task ExchangeCode_WithoutRefreshToken_ReturnsNullRefresh()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"access_token":"acc","expires_in":10}""");

        var tokens = await Client().ExchangeCodeAsync("c", CancellationToken.None);

        tokens.RefreshToken.ShouldBeNull();
        tokens.Scopes.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-84")]
    public async Task Refresh_PostsTheFormAndKeepsTheRefreshToken()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"access_token":"novo","expires_in":3600,"scope":"a b"}""");

        var tokens = await Client().RefreshAsync("ref-antigo", CancellationToken.None);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Uri.ToString().ShouldBe("https://oauth2.googleapis.com/token");
        Form(request).ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = "ref-antigo",
            ["client_id"] = "client-id-teste",
            ["client_secret"] = "client-secret-teste",
        });
        tokens.AccessToken.ShouldBe("novo");
        tokens.RefreshToken.ShouldBe("ref-antigo");
    }

    [Fact]
    [Trait("AC", "010-84")]
    public async Task InvalidGrant_BecomesTypedException_WithoutBodyInTheMessage()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");

        var ex = await Should.ThrowAsync<GoogleInvalidGrantException>(() => Client().RefreshAsync("ref-secreto", CancellationToken.None));

        ex.Message.ShouldNotContain("revoked");
        ex.Message.ShouldNotContain("ref-secreto");
    }

    [Theory]
    [InlineData(500, "{}")]
    [InlineData(400, """{"error":"invalid_client","error_description":"client-secret-teste"}""")]
    [InlineData(200, "isto nao e json")]
    [InlineData(200, """{"expires_in":3}""")]
    [Trait("AC", "010-89")]
    public async Task OtherFailures_BecomeGoogleAuthException_WithoutLeakingSecrets(int status, string body)
    {
        _handler.Enqueue((HttpStatusCode)status, body);

        var ex = await Should.ThrowAsync<GoogleAuthException>(() => Client().ExchangeCodeAsync("codigo-123", CancellationToken.None));

        ex.Message.ShouldNotContain("client-secret-teste");
        ex.Message.ShouldNotContain("codigo-123");
        ex.Message.ShouldNotContain("isto nao e json");
    }

    [Fact]
    public async Task NetworkFailure_BecomesGoogleAuthException()
    {
        _handler.EnqueueException(new HttpRequestException("dns client-secret-teste"));

        var ex = await Should.ThrowAsync<GoogleAuthException>(() => Client().ExchangeCodeAsync("c", CancellationToken.None));

        ex.Message.ShouldNotContain("client-secret-teste");
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Revoke_PostsTheTokenInTheBody_NotInTheUrl()
    {
        _handler.Enqueue(HttpStatusCode.OK, "{}");

        await Client().RevokeAsync("access-xyz", CancellationToken.None);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ToString().ShouldBe("https://oauth2.googleapis.com/revoke");
        request.Uri.Query.ShouldBeEmpty();
        request.Headers["Content-Type"].ShouldStartWith("application/x-www-form-urlencoded");
        request.BodyText.ShouldBe("token=access-xyz");
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Revoke_NonSuccess_ThrowsGoogleAuthException()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_token"}""");

        await Should.ThrowAsync<GoogleAuthException>(() => Client().RevokeAsync("access-xyz", CancellationToken.None));
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Revoke_TimesOutAfterTenSeconds()
    {
        _handler.EnqueueDelayed(TimeSpan.FromSeconds(30), HttpStatusCode.OK);
        var client = new GoogleOAuthClient(
            GoogleTestSupport.Settings(),
            new FakeGoogleHttpClientFactory(_handler),
            TimeProvider.System);

        var started = DateTime.UtcNow;
        await Should.ThrowAsync<GoogleAuthException>(() => client.RevokeAsync("t", CancellationToken.None));

        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(20));
    }
}
