using System.Net;
using System.Text.Json;
using System.Web;
using MeuSecretario.Api.Google;
using MeuSecretario.Application.Google;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Google;

/// <summary>Rotas do OAuth do Google ponta a ponta: HTTP real, PostgreSQL em container, Google e bot falsos (spec 010d, T29).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class GoogleRoutesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string DeadDatabase = "postgresql://u:p@127.0.0.1:1/x";
    private const string TokenResponse =
        """{"access_token":"acc-novo","refresh_token":"ref-novo","expires_in":3599,"scope":"https://www.googleapis.com/auth/calendar https://www.googleapis.com/auth/drive.file","token_type":"Bearer"}""";

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string LinkToken(GoogleTestHost host, int userId) => host.Services.GetRequiredService<ConnectLinkTokenCodec>().Create(userId);

    private static string State(GoogleTestHost host, int userId) => host.Services.GetRequiredService<OAuthStateCodec>().Create(userId);

    private static string Fixture(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "google-fixtures.json")))
            .RootElement.GetProperty("pages").GetProperty(name).GetString()!;

    private static async Task<(HttpResponseMessage Response, string Body)> Send(
        GoogleTestHost host, HttpMethod method, string path, string? apiKey = null)
    {
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (apiKey is not null)
        {
            request.Headers.Add("x-api-key", apiKey);
        }

        var response = await client.SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync());
    }

    private static void ShouldBeHtml(HttpResponseMessage response) =>
        response.Content.Headers.ContentType!.ToString().ShouldBe("text/html; charset=utf-8");

    // ============================== connect-calendar ==============================

    [Fact]
    [Trait("AC", "010-70")]
    public async Task Connect_WithDatabaseDown_Returns503WithTheExactJson()
    {
        await using var host = new GoogleTestHost(postgres, databaseUrl: DeadDatabase);

        var (response, body) = await host.Get($"/connect-calendar/7?t={LinkToken(host, 7)}");

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        body.ShouldBe("""{"status":"erro","resposta":"Banco de dados temporariamente indisponivel"}""");
    }

    [Fact]
    [Trait("AC", "010-70")]
    public async Task Connect_WithUnknownUser_Returns404Text()
    {
        await using var host = new GoogleTestHost(postgres);

        var (response, body) = await host.Get($"/connect-calendar/999?t={LinkToken(host, 999)}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe("Erro: Usuario nao encontrado");
        ShouldBeHtml(response);
    }

    [Fact]
    [Trait("AC", "010-71")]
    public async Task Connect_AlreadyConnectedWithDrive_Returns200WithThePythonHtml()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await GoogleDb.SeedTokens(postgres, uid);
        await using var host = new GoogleTestHost(postgres);

        var (response, body) = await host.Get($"/connect-calendar/{uid}?t={LinkToken(host, uid)}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBeHtml(response);
        body.ShouldBe(Fixture("already_connected"));
        host.Google.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-72")]
    public async Task Connect_NotConnected_Returns302ToGoogleWithASignedStateAndNoPkce()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await using var host = new GoogleTestHost(postgres);

        var (response, _) = await host.Get($"/connect-calendar/{uid}?t={LinkToken(host, uid)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.ShouldStartWith("https://accounts.google.com/o/oauth2/auth?");
        var query = HttpUtility.ParseQueryString(new Uri(location).Query);
        query["client_id"].ShouldBe("client-id-teste");
        query["redirect_uri"].ShouldBe(GoogleTestHost.RedirectUri);
        query["response_type"].ShouldBe("code");
        query["scope"].ShouldBe($"{GoogleTestHost.Calendar} {GoogleTestHost.Drive}");
        query["access_type"].ShouldBe("offline");
        query["include_granted_scopes"].ShouldBe("true");
        query["prompt"].ShouldBe("consent");
        location.ShouldNotContain("code_challenge");
        host.Services.GetRequiredService<OAuthStateCodec>().Verify(query["state"]).ShouldBe(uid);
        host.Google.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("?force=true", true)]
    [InlineData("?force=TRUE", false)]
    [InlineData("?force=1", false)]
    [InlineData("", false)]
    [Trait("AC", "010-73")]
    public async Task Connect_ConnectedWithDrive_OnlyForceTrueRedirects(string force, bool redirects)
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await GoogleDb.SeedTokens(postgres, uid);
        await using var host = new GoogleTestHost(postgres);
        var separator = force.Length == 0 ? "?" : force + "&";

        var (response, _) = await host.Get($"/connect-calendar/{uid}{separator}t={LinkToken(host, uid)}");

        response.StatusCode.ShouldBe(redirects ? HttpStatusCode.Redirect : HttpStatusCode.OK);
    }

    [Fact]
    [Trait("AC", "010-73")]
    public async Task Connect_ConnectedWithoutDrive_Returns302()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await GoogleDb.SeedTokens(postgres, uid, withDrive: false);
        await using var host = new GoogleTestHost(postgres);

        var (response, _) = await host.Get($"/connect-calendar/{uid}?t={LinkToken(host, uid)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    [Trait("AC", "010-74")]
    public async Task Connect_WithoutOAuthCredentials_Returns500WithTheFixedText()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await using var host = new GoogleTestHost(postgres, oauthConfigured: false);

        var (response, body) = await host.Get($"/connect-calendar/{uid}?t={LinkToken(host, uid)}");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("Erro ao conectar");
    }

    [Fact]
    [Trait("AC", "010-53")]
    public async Task Connect_WithoutAValidToken_Returns403Html_AndLeaksNothing()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await using var host = new GoogleTestHost(postgres);

        foreach (var path in new[]
        {
            $"/connect-calendar/{uid}",
            $"/connect-calendar/{uid}?t=",
            $"/connect-calendar/{uid}?t=lixo",
            $"/connect-calendar/{uid}?t={LinkToken(host, uid + 1)}",
            "/connect-calendar/999",
        })
        {
            var (response, body) = await host.Get(path);

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            ShouldBeHtml(response);
            body.ShouldNotContain("Usuario nao encontrado");
            response.Headers.Location.ShouldBeNull();
        }
    }

    [Fact]
    [Trait("AC", "010-53")]
    public async Task Connect_LegacyWindow_AcceptsLinksWithoutToken()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await using var host = new GoogleTestHost(postgres, legacyUntil: "2999-01-01");

        var (response, _) = await host.Get($"/connect-calendar/{uid}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    [Trait("AC", "010-53")]
    public async Task Connect_ExpiredLegacyWindow_IsForbidden()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await using var host = new GoogleTestHost(postgres, legacyUntil: "2000-01-01");

        var (response, _) = await host.Get($"/connect-calendar/{uid}");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ============================== oauth2callback ==============================

    [Fact]
    [Trait("AC", "010-76")]
    public async Task Callback_Denied_Returns200Html_EvenWithoutCode()
    {
        await using var host = new GoogleTestHost(postgres);

        var (response, body) = await host.Get("/oauth2callback?error=access_denied");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBeHtml(response);
        body.ShouldBe(Fixture("denied"));
    }

    [Theory]
    [InlineData("/oauth2callback")]
    [InlineData("/oauth2callback?code=abc")]
    [InlineData("/oauth2callback?state=abc")]
    [InlineData("/oauth2callback?code=&state=")]
    [Trait("AC", "010-76")]
    public async Task Callback_WithoutCodeOrState_Returns400Text(string path)
    {
        await using var host = new GoogleTestHost(postgres);

        var (response, body) = await host.Get(path);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe("Erro: codigo ou state faltando");
    }

    [Fact]
    [Trait("AC", "010-77")]
    public async Task Callback_Success_SavesTokens_NotifiesTheUser_AndRendersTheName()
    {
        var uid = await GoogleDb.SeedUser(postgres, "Maria", "5511999991072");
        await using var host = new GoogleTestHost(postgres);
        host.Google.Enqueue(HttpStatusCode.OK, TokenResponse);

        var (response, body) = await host.Get($"/oauth2callback?code=cod-123&state={Uri.EscapeDataString(State(host, uid))}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldBeHtml(response);
        body.ShouldBe(Fixture("connected").Replace("{nome}", "Maria", StringComparison.Ordinal));

        var request = host.Google.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Url.ShouldBe("https://oauth2.googleapis.com/token");
        var form = HttpUtility.ParseQueryString(request.Body!);
        form["grant_type"].ShouldBe("authorization_code");
        form["code"].ShouldBe("cod-123");
        form["redirect_uri"].ShouldBe(GoogleTestHost.RedirectUri);
        form["client_id"].ShouldBe("client-id-teste");
        form["client_secret"].ShouldBe("client-secret-teste");
        request.Body!.ShouldNotContain("code_verifier");

        (await GoogleDb.Scalar<string>(postgres, "SELECT access_token FROM googlecalendartokens WHERE usuario_id = @uid", new { uid })).ShouldBe("acc-novo");
        (await GoogleDb.Scalar<string>(postgres, "SELECT refresh_token FROM googlecalendartokens WHERE usuario_id = @uid", new { uid })).ShouldBe("ref-novo");
        (await GoogleDb.Scalar<bool>(postgres, "SELECT needs_reconnect FROM googlecalendartokens WHERE usuario_id = @uid", new { uid })).ShouldBeFalse();
        var scopes = await GoogleDb.Scalar<string>(postgres, "SELECT scopes FROM googlecalendartokens WHERE usuario_id = @uid", new { uid });
        JsonSerializer.Deserialize<string[]>(scopes).ShouldBe([GoogleTestHost.Calendar, GoogleTestHost.Drive]);
        var expiry = await GoogleDb.Scalar<DateTime>(postgres, "SELECT token_expiry FROM googlecalendartokens WHERE usuario_id = @uid", new { uid });
        (expiry - DateTime.UtcNow).ShouldBeInRange(TimeSpan.FromSeconds(3500), TimeSpan.FromSeconds(3600));

        var sent = host.Bot.Sent.ShouldHaveSingleItem();
        sent.Number.ShouldBe("5511999991072");
        sent.Message.ShouldBe(GoogleNotificationMessages.Connected("Maria"));
    }

    [Fact]
    [Trait("AC", "010-78")]
    public async Task Callback_Reconnect_KeepsTheOldRefreshToken_WhenGoogleSendsNone()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await GoogleDb.SeedTokens(postgres, uid, refresh: "ref-antigo", needsReconnect: true);
        await using var host = new GoogleTestHost(postgres);
        host.Google.Enqueue(HttpStatusCode.OK, """{"access_token":"acc-novo","expires_in":3599,"scope":"https://www.googleapis.com/auth/calendar"}""");

        var (response, _) = await host.Get($"/oauth2callback?code=c&state={Uri.EscapeDataString(State(host, uid))}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GoogleDb.Scalar<string>(postgres, "SELECT refresh_token FROM googlecalendartokens WHERE usuario_id = @uid", new { uid })).ShouldBe("ref-antigo");
        (await GoogleDb.Scalar<bool>(postgres, "SELECT needs_reconnect FROM googlecalendartokens WHERE usuario_id = @uid", new { uid })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "010-79")]
    public async Task Callback_UserWithoutNumber_DoesNotCallTheBot_AndBotFailureStill200()
    {
        var withoutNumber = await GoogleDb.SeedUser(postgres, "Sem Numero", whatsapp: "");
        var withNumber = await GoogleDb.SeedUser(postgres, "Com Numero", "5511999990002");
        await using var host = new GoogleTestHost(postgres);
        host.Bot.Succeeds = false;
        host.Google.Enqueue(HttpStatusCode.OK, TokenResponse).Enqueue(HttpStatusCode.OK, TokenResponse);

        var (first, _) = await host.Get($"/oauth2callback?code=c&state={Uri.EscapeDataString(State(host, withoutNumber))}");
        host.Bot.Sent.ShouldBeEmpty();
        var (second, _) = await host.Get($"/oauth2callback?code=c&state={Uri.EscapeDataString(State(host, withNumber))}");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Bot.Sent.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("Mw==")]
    [InlineData("lixo.assinatura")]
    [Trait("AC", "010-80")]
    public async Task Callback_InvalidState_Returns500Html_WithoutGoogleOrRow(string state)
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await using var host = new GoogleTestHost(postgres);

        var (response, body) = await host.Get($"/oauth2callback?code=c&state={state}");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        ShouldBeHtml(response);
        body.ShouldBe(Fixture("error").Replace("{texto}", "link de autorizacao invalido ou expirado", StringComparison.Ordinal));
        host.Google.Requests.ShouldBeEmpty();
        (await GoogleDb.RowCount(postgres)).ShouldBe(0);
        uid.ShouldBeGreaterThan(0);
    }

    [Fact]
    [Trait("AC", "010-80")]
    public async Task Callback_GoogleRefusesTheCode_Returns500InternalError_WithoutRow_AndNoLeak()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await using var host = new GoogleTestHost(postgres);
        host.Google.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Bad Request segredo-do-google"}""");

        var (response, body) = await host.Get($"/oauth2callback?code=c&state={Uri.EscapeDataString(State(host, uid))}");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Fixture("error").Replace("{texto}", "erro interno", StringComparison.Ordinal));
        body.ShouldNotContain("segredo-do-google");
        body.ShouldNotContain("client-secret-teste");
        (await GoogleDb.RowCount(postgres)).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "010-80")]
    public async Task Callback_StateForAMissingUser_ViolatesTheForeignKey_Returns500_WithoutLeak()
    {
        await using var host = new GoogleTestHost(postgres);
        host.Google.Enqueue(HttpStatusCode.OK, TokenResponse);

        var (response, body) = await host.Get($"/oauth2callback?code=c&state={Uri.EscapeDataString(State(host, 424242))}");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(Fixture("error").Replace("{texto}", "erro interno", StringComparison.Ordinal));
        body.ShouldNotContain("23503");
        host.Bot.Sent.ShouldBeEmpty();
    }

    // ============================== disconnect-calendar ==============================

    private const string Unauthorized = """{"status":"erro","mensagem":"Não autorizado"}""";

    [Theory]
    [InlineData(null)]
    [InlineData("chave-errada")]
    [Trait("AC", "010-81")]
    public async Task Disconnect_WithoutOrWithWrongApiKey_Returns401_AndKeepsTheRow(string? apiKey)
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await GoogleDb.SeedTokens(postgres, uid);
        await using var host = new GoogleTestHost(postgres);

        var (response, body) = await Send(host, HttpMethod.Post, $"/disconnect-calendar/{uid}", apiKey);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(Unauthorized);
        (await GoogleDb.RowCount(postgres)).ShouldBe(1);
        host.Google.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Disconnect_WithKey_RevokesAtGoogle_DeletesTheRow_AndReturns200()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await GoogleDb.SeedTokens(postgres, uid, access: "acc-para-revogar");
        await using var host = new GoogleTestHost(postgres);

        var (response, body) = await Send(host, HttpMethod.Post, $"/disconnect-calendar/{uid}", AuthFixtures.JwtSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","mensagem":"Google Calendar desconectado com sucesso"}""");
        var revoke = host.Google.Requests.ShouldHaveSingleItem();
        revoke.Method.ShouldBe(HttpMethod.Post);
        revoke.Url.ShouldBe("https://oauth2.googleapis.com/revoke");
        revoke.Body.ShouldBe("token=acc-para-revogar");
        (await GoogleDb.RowCount(postgres)).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Disconnect_WithoutRow_OrUnknownUser_Returns200()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await using var host = new GoogleTestHost(postgres);

        var (known, _) = await Send(host, HttpMethod.Post, $"/disconnect-calendar/{uid}", AuthFixtures.JwtSecret);
        var (unknown, _) = await Send(host, HttpMethod.Post, "/disconnect-calendar/99999", AuthFixtures.JwtSecret);

        known.StatusCode.ShouldBe(HttpStatusCode.OK);
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);
        host.Google.Requests.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Disconnect_WhenGoogleRevokeFails_StillDeletesAndReturns200()
    {
        var uid = await GoogleDb.SeedUser(postgres);
        await GoogleDb.SeedTokens(postgres, uid);
        await using var host = new GoogleTestHost(postgres);
        host.Google.Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_token"}""");

        var (response, _) = await Send(host, HttpMethod.Post, $"/disconnect-calendar/{uid}", AuthFixtures.JwtSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GoogleDb.RowCount(postgres)).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Disconnect_WithDatabaseError_Returns500FixedText()
    {
        await using var host = new GoogleTestHost(postgres, databaseUrl: DeadDatabase);

        var (response, body) = await Send(host, HttpMethod.Post, "/disconnect-calendar/7", AuthFixtures.JwtSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe("""{"status":"erro","mensagem":"Erro ao desconectar Google Calendar"}""");
    }

    // ============================== roteamento ==============================

    [Theory]
    [InlineData("/connect-calendar/abc")]
    [InlineData("/connect-calendar/-1")]
    [InlineData("/connect-calendar/1/")]
    [InlineData("/connect-calendar/99999999999")]
    [Trait("AC", "010-82")]
    public async Task Connect_InvalidIds_Return404(string path)
    {
        await using var host = new GoogleTestHost(postgres);

        var (response, _) = await host.Get(path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("AC", "010-82")]
    public async Task Connect_OverflowingId_Returns404FlaskHtml()
    {
        await using var host = new GoogleTestHost(postgres);

        var (response, body) = await host.Get("/connect-calendar/99999999999");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("<h1>Not Found</h1>");
    }

    [Theory]
    [InlineData("POST", "/connect-calendar/1")]
    [InlineData("GET", "/disconnect-calendar/1")]
    [InlineData("POST", "/oauth2callback")]
    [Trait("AC", "010-82")]
    public async Task WrongMethods_Return405(string method, string path)
    {
        await using var host = new GoogleTestHost(postgres);

        var (response, _) = await Send(host, new HttpMethod(method), path, AuthFixtures.JwtSecret);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
