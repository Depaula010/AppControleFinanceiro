using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Google;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.Google;

public class GoogleAccountServiceTests
{
    private const string Calendar = "https://www.googleapis.com/auth/calendar";
    private const string Drive = "https://www.googleapis.com/auth/drive.file";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly IGoogleTokenRepository _tokens = Substitute.For<IGoogleTokenRepository>();
    private readonly IGoogleOAuthClient _oauth = Substitute.For<IGoogleOAuthClient>();
    private readonly IGoogleUserLookup _users = Substitute.For<IGoogleUserLookup>();
    private readonly IBotClient _bot = Substitute.For<IBotClient>();
    private readonly IGoogleDiagnostics _diagnostics = Substitute.For<IGoogleDiagnostics>();
    private readonly InMemoryCache _cache;
    private readonly GoogleAccountService _service;

    public GoogleAccountServiceTests()
    {
        _cache = new InMemoryCache(_time);
        var settings = GoogleTestSupport.Settings();
        var links = new GoogleLinks(settings, new ConnectLinkTokenCodec(settings, _time));
        var notifier = new TokenRevokedNotifier(_cache, _users, _bot, links, _diagnostics);
        _service = new GoogleAccountService(_tokens, _oauth, new OAuthStateCodec(settings, _time), notifier, _time, _diagnostics);
        _users.FindAsync(7, Arg.Any<CancellationToken>()).Returns(new GoogleUserInfo("Maria", "5511999991072"));
        _bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private static string ScopesJson => $"[\"{Calendar}\", \"{Drive}\"]";

    private DateTime In(TimeSpan span) => _time.GetUtcNow().UtcDateTime + span;

    private void Stored(DateTime? expiry, string? refresh = "ref-1", bool needsReconnect = false, string? scopes = null) =>
        _tokens.FindAsync(7, Arg.Any<CancellationToken>())
            .Returns(new GoogleTokenRecord("acc-1", refresh, expiry, scopes ?? ScopesJson, needsReconnect));

    private Task<GoogleCredentialResult> Credentials() => _service.GetCredentialsAsync(7, CancellationToken.None);

    [Fact]
    [Trait("AC", "010-83")]
    public async Task NoRow_IsNotConnected_WithoutCallingGoogle()
    {
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).Returns((GoogleTokenRecord?)null);

        (await Credentials()).Status.ShouldBe(GoogleCredentialStatus.NotConnected);

        await _oauth.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "010-83")]
    public async Task NotExpiredToken_IsReturnedAsIs()
    {
        Stored(In(TimeSpan.FromMinutes(30)));

        var result = await Credentials();

        result.Status.ShouldBe(GoogleCredentialStatus.Connected);
        result.AccessToken.ShouldBe("acc-1");
        result.Scopes.ShouldBe([Calendar, Drive]);
        await _oauth.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "010-83")]
    public async Task NullExpiry_IsTreatedAsNotExpired()
    {
        Stored(expiry: null);

        (await Credentials()).Status.ShouldBe(GoogleCredentialStatus.Connected);
        await _oauth.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    public async Task MissingScopes_FallBackToTheTwoDefaultScopes()
    {
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).Returns(new GoogleTokenRecord("acc-1", "r", null, null, false));

        (await Credentials()).Scopes.ShouldBe([Calendar, Drive]);
    }

    [Fact]
    [Trait("AC", "010-84")]
    public async Task ExpiredToken_IsRefreshedAndSaved_KeepingScopesAndRefreshToken()
    {
        Stored(In(TimeSpan.FromMinutes(1))); // dentro da margem de 3 min 45 s
        var expiresAt = _time.GetUtcNow().AddHours(1);
        _oauth.RefreshAsync("ref-1", Arg.Any<CancellationToken>()).Returns(new GoogleTokenSet("acc-2", "ref-1", expiresAt, []));

        var result = await Credentials();

        result.Status.ShouldBe(GoogleCredentialStatus.Connected);
        result.AccessToken.ShouldBe("acc-2");
        await _tokens.Received(1).SaveAsync(
            7,
            Arg.Is<GoogleTokenSet>(t => t.AccessToken == "acc-2" && t.RefreshToken == "ref-1" && t.ExpiresAtUtc == expiresAt
                && t.Scopes.SequenceEqual(new[] { Calendar, Drive })),
            Arg.Any<CancellationToken>());
        await _tokens.DidNotReceiveWithAnyArgs().MarkNeedsReconnectAsync(default, default);
    }

    [Fact]
    [Trait("AC", "010-85")]
    public async Task InvalidGrant_MarksNotifiesAndReturnsNeedsReconnect()
    {
        Stored(In(TimeSpan.FromMinutes(-5)));
        _oauth.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new GoogleInvalidGrantException());

        (await Credentials()).Status.ShouldBe(GoogleCredentialStatus.NeedsReconnect);

        await _tokens.Received(1).MarkNeedsReconnectAsync(7, Arg.Any<CancellationToken>());
        await _bot.Received(1).SendTextAsync("5511999991072", Arg.Any<string>(), Arg.Any<CancellationToken>());
        _cache.RawValue(CacheKeys.CalendarTokenRevokedAlert(7)).ShouldBe("\"1\"");
        await _tokens.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default);
    }

    [Fact]
    [Trait("AC", "010-85")]
    public async Task TemporaryRefreshFailure_ReturnsUnavailable_WithoutMarkingOrNotifying()
    {
        Stored(In(TimeSpan.FromMinutes(-5)));
        _oauth.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new GoogleAuthException("HTTP 500"));

        (await Credentials()).Status.ShouldBe(GoogleCredentialStatus.Unavailable);

        await _tokens.DidNotReceiveWithAnyArgs().MarkNeedsReconnectAsync(default, default);
        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [Trait("AC", "010-85")]
    public async Task ExpiredWithoutRefreshToken_MarksAndNotifies(string? refresh)
    {
        Stored(In(TimeSpan.FromMinutes(-5)), refresh);

        (await Credentials()).Status.ShouldBe(GoogleCredentialStatus.NeedsReconnect);

        await _tokens.Received(1).MarkNeedsReconnectAsync(7, Arg.Any<CancellationToken>());
        await _bot.Received(1).SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _oauth.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
    }

    [Fact]
    public async Task Deviation058_NeedsReconnectAndExpired_DoesNotCallGoogle()
    {
        Stored(In(TimeSpan.FromMinutes(-5)), needsReconnect: true);

        (await Credentials()).Status.ShouldBe(GoogleCredentialStatus.NeedsReconnect);

        await _oauth.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    public async Task Deviation058_NeedsReconnectButNotExpired_StillReturnsTheToken()
    {
        Stored(In(TimeSpan.FromMinutes(30)), needsReconnect: true);

        (await Credentials()).Status.ShouldBe(GoogleCredentialStatus.Connected);
    }

    [Fact]
    public async Task IsConnected_FollowsTheCredentialStatus()
    {
        Stored(In(TimeSpan.FromMinutes(30)));
        (await _service.IsConnectedAsync(7, CancellationToken.None)).ShouldBeTrue();

        Stored(In(TimeSpan.FromMinutes(-5)), needsReconnect: true);
        (await _service.IsConnectedAsync(7, CancellationToken.None)).ShouldBeFalse();
    }

    // ---- notificação (AC-010-86) ----

    private Task Notify() => new TokenRevokedNotifier(
        _cache, _users, _bot, new GoogleLinks(GoogleTestSupport.Settings(), new ConnectLinkTokenCodec(GoogleTestSupport.Settings(), _time)), _diagnostics)
        .NotifyAsync(7, CancellationToken.None);

    [Fact]
    [Trait("AC", "010-86")]
    public async Task Notification_SendsWithLink_AndStoresTheKeyForSevenDays()
    {
        await Notify();

        var call = _bot.ReceivedCalls().ShouldHaveSingleItem();
        call.GetArguments()[0].ShouldBe("5511999991072");
        var sent = (string)call.GetArguments()[1]!;
        sent.ShouldContain("https://h.exemplo/connect-calendar/7?t=");
        sent.ShouldContain("👉 *Reconecte agora:*");
        _cache.RawValue("calendar_token_revoked_alert:7").ShouldBe("\"1\"");
        _cache.TtlOf("calendar_token_revoked_alert:7").ShouldBe(TimeSpan.FromDays(7));
    }

    [Fact]
    [Trait("AC", "010-86")]
    public async Task Notification_WithoutRedirectUri_UsesTheTextWithoutLink()
    {
        var settings = GoogleTestSupport.Settings(redirectUri: "");
        var notifier = new TokenRevokedNotifier(_cache, _users, _bot, new GoogleLinks(settings, new ConnectLinkTokenCodec(settings, _time)), _diagnostics);

        await notifier.NotifyAsync(7, CancellationToken.None);

        await _bot.Received(1).SendTextAsync(
            "5511999991072", GoogleNotificationMessages.TokenRevoked(null), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "010-86")]
    public async Task Notification_IsThrottledByTheExistingKey()
    {
        await _cache.SetJsonAsync(CacheKeys.CalendarTokenRevokedAlert(7), "1", TimeSpan.FromDays(1));

        await Notify();

        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [Trait("AC", "010-86")]
    public async Task Notification_WithoutNumber_DoesNothing(string? number)
    {
        _users.FindAsync(7, Arg.Any<CancellationToken>()).Returns(new GoogleUserInfo("Maria", number));

        await Notify();

        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
        _cache.RawValue(CacheKeys.CalendarTokenRevokedAlert(7)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-86")]
    public async Task Notification_WhenSendFails_DoesNotStoreTheKey()
    {
        _bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await Notify();

        _cache.RawValue(CacheKeys.CalendarTokenRevokedAlert(7)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-86")]
    public async Task Notification_InternalErrors_NeverPropagate()
    {
        _users.FindAsync(7, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("banco caiu"));

        await Should.NotThrowAsync(Notify);

        _diagnostics.Received(1).NotificationFailed(7, Arg.Any<Exception>());
    }

    // ---- escopo do Drive (AC-010-87) ----

    [Theory]
    [InlineData("[\"https://www.googleapis.com/auth/calendar\", \"https://www.googleapis.com/auth/drive.file\"]", true)]
    [InlineData("[\"https://www.googleapis.com/auth/calendar\"]", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("{nao e array", false)]
    [Trait("AC", "010-87")]
    public async Task HasDriveScope_ReadsTheStoredArray(string? stored, bool expected)
    {
        _tokens.GetScopesAsync(7, Arg.Any<CancellationToken>()).Returns(stored);

        (await _service.HasDriveScopeAsync(7, CancellationToken.None)).ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "010-87")]
    public async Task HasDriveScope_OnAnyError_IsFalse()
    {
        _tokens.GetScopesAsync(7, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException());

        (await _service.HasDriveScopeAsync(7, CancellationToken.None)).ShouldBeFalse();
    }

    // ---- revogação (AC-010-81) ----

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Revoke_RevokesAtGoogleAndDeletesTheRow()
    {
        Stored(In(TimeSpan.FromMinutes(30)));

        await _service.RevokeAsync(7, CancellationToken.None);

        await _oauth.Received(1).RevokeAsync("acc-1", Arg.Any<CancellationToken>());
        await _tokens.Received(1).DeleteAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Revoke_WithoutRow_StillSucceeds_WithoutCallingGoogle()
    {
        _tokens.FindAsync(7, Arg.Any<CancellationToken>()).Returns((GoogleTokenRecord?)null);

        (await _service.RevokeAsync(7, CancellationToken.None)).ShouldBeTrue();

        await _oauth.DidNotReceiveWithAnyArgs().RevokeAsync(default!, default);
        await _tokens.Received(1).DeleteAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "010-81")]
    public async Task Revoke_WhenGoogleFails_StillDeletesTheRow()
    {
        Stored(In(TimeSpan.FromMinutes(30)));
        _oauth.RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new GoogleAuthException("HTTP 400"));

        (await _service.RevokeAsync(7, CancellationToken.None)).ShouldBeTrue();

        await _tokens.Received(1).DeleteAsync(7, Arg.Any<CancellationToken>());
    }

    // ---- state e troca (AC-010-77, 80) ----

    [Fact]
    [Trait("AC", "010-77")]
    public async Task ExchangeAndSave_ValidatesTheState_ThenExchangesAndSaves()
    {
        var url = "";
        _oauth.BuildAuthorizationUrl(Arg.Do<string>(s => url = s)).Returns("https://accounts.google.com/x");
        _service.BuildAuthorizationUrl(7);
        var tokenSet = new GoogleTokenSet("a", "r", _time.GetUtcNow().AddHours(1), [Calendar]);
        _oauth.ExchangeCodeAsync("cod", Arg.Any<CancellationToken>()).Returns(tokenSet);

        var userId = await _service.ExchangeAndSaveAsync("cod", url, CancellationToken.None);

        userId.ShouldBe(7);
        await _tokens.Received(1).SaveAsync(7, tokenSet, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Nw==")]
    [InlineData("lixo.assinatura")]
    [Trait("AC", "010-80")]
    public async Task ExchangeAndSave_WithInvalidState_NeverCallsGoogleNorSaves(string state)
    {
        (await _service.ExchangeAndSaveAsync("cod", state, CancellationToken.None)).ShouldBeNull();

        await _oauth.DidNotReceiveWithAnyArgs().ExchangeCodeAsync(default!, default);
        await _tokens.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, default);
    }
}
