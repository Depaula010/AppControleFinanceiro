using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Google;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.Google;

public class GoogleUseCasesTests
{
    private const string Calendar = "https://www.googleapis.com/auth/calendar";
    private const string Drive = "https://www.googleapis.com/auth/drive.file";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly IGoogleTokenRepository _tokens = Substitute.For<IGoogleTokenRepository>();
    private readonly IGoogleOAuthClient _oauth = Substitute.For<IGoogleOAuthClient>();
    private readonly IGoogleUserLookup _users = Substitute.For<IGoogleUserLookup>();
    private readonly IBotClient _bot = Substitute.For<IBotClient>();
    private readonly IDatabaseHealth _database = Substitute.For<IDatabaseHealth>();
    private readonly IGoogleDiagnostics _diagnostics = Substitute.For<IGoogleDiagnostics>();
    private readonly IIntegrationSettings _settings;
    private readonly ConnectLinkTokenCodec _linkTokens;
    private readonly GoogleAccountService _account;

    public GoogleUseCasesTests()
    {
        _settings = GoogleTestSupport.Settings();
        _linkTokens = new ConnectLinkTokenCodec(_settings, _time);
        var links = new GoogleLinks(_settings, _linkTokens);
        var notifier = new TokenRevokedNotifier(new InMemoryCache(_time), _users, _bot, links, _diagnostics);
        _account = new GoogleAccountService(_tokens, _oauth, new OAuthStateCodec(_settings, _time), notifier, _time, _diagnostics);
        _database.CheckAsync(Arg.Any<CancellationToken>()).Returns(new DatabaseStatus(true));
        _users.FindAsync(7, Arg.Any<CancellationToken>()).Returns(new GoogleUserInfo("Maria", "5511999991072"));
        _oauth.BuildAuthorizationUrl(Arg.Any<string>()).Returns(call => "https://accounts.google.com/o/oauth2/auth?state=" + call.Arg<string>());
        _bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private ConnectCalendar Connect(IIntegrationSettings? settings = null) =>
        new(_database, _users, _account, _linkTokens, settings ?? _settings, _time);

    private string Token(int userId = 7) => _linkTokens.Create(userId);

    private void Connected(bool withDrive)
    {
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).Returns(
            new GoogleTokenRecord("acc", "ref", _time.GetUtcNow().UtcDateTime.AddHours(1), null, false));
        _tokens.GetScopesAsync(7, Arg.Any<CancellationToken>()).Returns(withDrive ? $"[\"{Calendar}\", \"{Drive}\"]" : $"[\"{Calendar}\"]");
    }

    // ---- ConnectCalendar ----

    [Fact]
    [Trait("AC", "010-70")]
    public async Task Connect_WithDatabaseDown_IsUnavailable()
    {
        _database.CheckAsync(Arg.Any<CancellationToken>()).Returns(new DatabaseStatus(false, "NpgsqlException"));

        (await Connect().ExecuteAsync(7, Token(), false, CancellationToken.None)).ShouldBeOfType<ConnectResult.DatabaseUnavailable>();
    }

    [Fact]
    [Trait("AC", "010-70")]
    public async Task Connect_WithUnknownUser_IsNotFound()
    {
        _users.FindAsync(8, Arg.Any<CancellationToken>()).Returns((GoogleUserInfo?)null);

        (await Connect().ExecuteAsync(8, Token(8), false, CancellationToken.None)).ShouldBeOfType<ConnectResult.UserNotFound>();
    }

    [Fact]
    [Trait("AC", "010-71")]
    public async Task Connect_ConnectedWithDriveAndNoForce_IsAlreadyConnected()
    {
        Connected(withDrive: true);

        (await Connect().ExecuteAsync(7, Token(), false, CancellationToken.None)).ShouldBeOfType<ConnectResult.AlreadyConnected>();
    }

    [Fact]
    [Trait("AC", "010-72")]
    public async Task Connect_NotConnected_RedirectsWithASignedState()
    {
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).Returns((GoogleTokenRecord?)null);

        var result = await Connect().ExecuteAsync(7, Token(), false, CancellationToken.None);

        var redirect = result.ShouldBeOfType<ConnectResult.Redirect>();
        var state = redirect.Url["https://accounts.google.com/o/oauth2/auth?state=".Length..];
        new OAuthStateCodec(_settings, _time).Verify(state).ShouldBe(7);
        redirect.ToString().ShouldNotContain("state=");
    }

    [Fact]
    [Trait("AC", "010-73")]
    public async Task Connect_ConnectedWithoutDrive_Redirects()
    {
        Connected(withDrive: false);

        (await Connect().ExecuteAsync(7, Token(), false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Redirect>();
    }

    [Fact]
    [Trait("AC", "010-73")]
    public async Task Connect_WithForce_RedirectsEvenWhenFullyConnected()
    {
        Connected(withDrive: true);

        (await Connect().ExecuteAsync(7, Token(), true, CancellationToken.None)).ShouldBeOfType<ConnectResult.Redirect>();
    }

    [Fact]
    [Trait("AC", "010-74")]
    public async Task Connect_WithoutOAuthCredentials_Fails()
    {
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).Returns((GoogleTokenRecord?)null);
        _oauth.BuildAuthorizationUrl(Arg.Any<string>()).Throws(new GoogleOAuthConfigurationException());

        var result = await Connect().ExecuteAsync(7, Token(), false, CancellationToken.None);

        result.ShouldBeOfType<ConnectResult.Failed>().Error.ShouldBeOfType<GoogleOAuthConfigurationException>();
    }

    [Fact]
    public async Task Connect_WhenTheDatabaseThrows_Fails()
    {
        _users.FindAsync(7, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        (await Connect().ExecuteAsync(7, Token(), false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Failed>();
    }

    [Fact]
    [Trait("AC", "010-53")]
    public async Task Connect_WithoutToken_IsForbidden_AndNothingIsRead()
    {
        (await Connect().ExecuteAsync(7, null, false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Forbidden>();
        (await Connect().ExecuteAsync(7, "", false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Forbidden>();

        await _database.DidNotReceiveWithAnyArgs().CheckAsync(default);
        await _users.DidNotReceiveWithAnyArgs().FindAsync(default, default);
    }

    [Fact]
    [Trait("AC", "010-53")]
    public async Task Connect_WithAnotherUsersToken_OrGarbage_OrExpired_IsForbidden()
    {
        (await Connect().ExecuteAsync(7, Token(8), false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Forbidden>();
        (await Connect().ExecuteAsync(7, "lixo", false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Forbidden>();

        var token = Token();
        _time.Advance(TimeSpan.FromDays(7) + TimeSpan.FromSeconds(1));
        (await Connect().ExecuteAsync(7, token, false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Forbidden>();
    }

    [Fact]
    [Trait("AC", "010-53")]
    public async Task Connect_LegacyWindow_AcceptsLinksWithoutTokenUntilTheDate_ButNeverABadToken()
    {
        var legacy = GoogleTestSupport.Settings(legacyUntil: _time.GetUtcNow().AddDays(1));
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).Returns((GoogleTokenRecord?)null);

        (await Connect(legacy).ExecuteAsync(7, null, false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Redirect>();
        (await Connect(legacy).ExecuteAsync(7, "lixo", false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Forbidden>();

        _time.Advance(TimeSpan.FromDays(1));
        (await Connect(legacy).ExecuteAsync(7, null, false, CancellationToken.None)).ShouldBeOfType<ConnectResult.Forbidden>();
    }

    // ---- HandleOAuthCallback ----

    private HandleOAuthCallback Callback() => new(_account, _users, _bot);

    private string ValidState(int userId = 7) => new OAuthStateCodec(_settings, _time).Create(userId);

    private void GoogleAcceptsTheCode() =>
        _oauth.ExchangeCodeAsync("cod", Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenSet("a", "r", _time.GetUtcNow().AddHours(1), [Calendar, Drive]));

    [Fact]
    [Trait("AC", "010-76")]
    public async Task Callback_WithError_IsDenied_EvenWithoutCode()
    {
        (await Callback().ExecuteAsync(null, null, "access_denied", CancellationToken.None)).ShouldBeOfType<CallbackResult.Denied>();
        (await Callback().ExecuteAsync("cod", ValidState(), "access_denied", CancellationToken.None)).ShouldBeOfType<CallbackResult.Denied>();
    }

    [Theory]
    [InlineData(null, "s")]
    [InlineData("", "s")]
    [InlineData("c", null)]
    [InlineData("c", "")]
    [InlineData(null, null)]
    [Trait("AC", "010-76")]
    public async Task Callback_WithoutCodeOrState_IsMissingParams(string? code, string? state)
    {
        (await Callback().ExecuteAsync(code, state, null, CancellationToken.None)).ShouldBeOfType<CallbackResult.MissingParams>();
        (await Callback().ExecuteAsync(code, state, "", CancellationToken.None)).ShouldBeOfType<CallbackResult.MissingParams>();
    }

    [Fact]
    [Trait("AC", "010-77")]
    public async Task Callback_Success_SavesTokensThenNotifiesTheUserAndReturnsTheName()
    {
        GoogleAcceptsTheCode();

        var result = await Callback().ExecuteAsync("cod", ValidState(), null, CancellationToken.None);

        result.ShouldBeOfType<CallbackResult.Connected>().UserName.ShouldBe("Maria");
        Received.InOrder(() =>
        {
            _tokens.SaveAsync(7, Arg.Any<GoogleTokenSet>(), Arg.Any<CancellationToken>());
            _bot.SendTextAsync("5511999991072", GoogleNotificationMessages.Connected("Maria"), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    [Trait("AC", "010-79")]
    public async Task Callback_UserWithoutNumber_NeverCallsTheBot()
    {
        GoogleAcceptsTheCode();
        _users.FindAsync(7, Arg.Any<CancellationToken>()).Returns(new GoogleUserInfo("Maria", null));

        (await Callback().ExecuteAsync("cod", ValidState(), null, CancellationToken.None)).ShouldBeOfType<CallbackResult.Connected>();

        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "010-79")]
    public async Task Callback_BotFailure_DoesNotChangeTheOutcome()
    {
        GoogleAcceptsTheCode();
        _bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        (await Callback().ExecuteAsync("cod", ValidState(), null, CancellationToken.None)).ShouldBeOfType<CallbackResult.Connected>();

        _bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new HttpRequestException());
        (await Callback().ExecuteAsync("cod", ValidState(), null, CancellationToken.None)).ShouldBeOfType<CallbackResult.Connected>();
    }

    [Fact]
    public async Task Callback_UnknownUserAfterSave_UsesTheFallbackName()
    {
        GoogleAcceptsTheCode();
        _users.FindAsync(7, Arg.Any<CancellationToken>()).Returns((GoogleUserInfo?)null);

        var result = await Callback().ExecuteAsync("cod", ValidState(), null, CancellationToken.None);

        result.ShouldBeOfType<CallbackResult.Connected>().UserName.ShouldBe("Usuario");
        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("N0==")]
    [InlineData("lixo.assinatura")]
    [InlineData("expirado")]
    [Trait("AC", "010-80")]
    public async Task Callback_InvalidOrExpiredState_IsInvalidState_WithoutGoogleOrSave(string state)
    {
        if (state == "expirado")
        {
            state = ValidState();
            _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        }

        (await Callback().ExecuteAsync("cod", state, null, CancellationToken.None)).ShouldBeOfType<CallbackResult.InvalidState>();

        await _oauth.DidNotReceiveWithAnyArgs().ExchangeCodeAsync(default!, default);
        await _tokens.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default);
    }

    [Fact]
    [Trait("AC", "010-80")]
    public async Task Callback_GoogleRefusesTheCode_Fails_WithoutSaving()
    {
        _oauth.ExchangeCodeAsync("cod", Arg.Any<CancellationToken>()).ThrowsAsync(new GoogleInvalidGrantException());

        (await Callback().ExecuteAsync("cod", ValidState(), null, CancellationToken.None)).ShouldBeOfType<CallbackResult.Failed>();

        await _tokens.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default);
        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "010-80")]
    public async Task Callback_DatabaseFailureOnSave_Fails_WithoutNotifying()
    {
        GoogleAcceptsTheCode();
        _tokens.SaveAsync(7, Arg.Any<GoogleTokenSet>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fk"));

        (await Callback().ExecuteAsync("cod", ValidState(), null, CancellationToken.None)).ShouldBeOfType<CallbackResult.Failed>();

        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    // ---- DisconnectCalendar ----

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Disconnect_RevokesAndDeletes()
    {
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).Returns(
            new GoogleTokenRecord("acc", "ref", _time.GetUtcNow().UtcDateTime.AddHours(1), null, false));

        (await new DisconnectCalendar(_account).ExecuteAsync(7, CancellationToken.None)).ShouldBeOfType<DisconnectResult.Disconnected>();

        await _oauth.Received(1).RevokeAsync("acc", Arg.Any<CancellationToken>());
        await _tokens.Received(1).DeleteAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Disconnect_WithDatabaseError_Fails()
    {
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        (await new DisconnectCalendar(_account).ExecuteAsync(7, CancellationToken.None)).ShouldBeOfType<DisconnectResult.Failed>();
    }
}
