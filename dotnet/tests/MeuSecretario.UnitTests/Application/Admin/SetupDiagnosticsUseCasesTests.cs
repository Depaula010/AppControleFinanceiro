using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Admin;

public class GetOAuthConfigCheckTests
{
    private const string ClientId = "CLIENT-ID-SENTINELA-123456789.apps.googleusercontent.com";
    private const string ClientSecret = "SECRET-SENTINELA-xyz";

    private static OAuthConfigCheck Run(string id, string secret, string redirect = "https://api.exemplo.com/callback")
    {
        var settings = Substitute.For<IIntegrationSettings>();
        settings.GoogleClientId.Returns(id);
        settings.GoogleClientSecret.Returns(secret);
        settings.GoogleRedirectUri.Returns(redirect);
        return new GetOAuthConfigCheck(settings).Execute();
    }

    [Fact]
    public void Configured_ReportsTrue_AndKeepsTheRedirectUri()
    {
        var check = Run(ClientId, ClientSecret);

        check.ClientIdConfigured.ShouldBeTrue();
        check.ClientSecretConfigured.ShouldBeTrue();
        check.RedirectUri.ShouldBe("https://api.exemplo.com/callback");
        check.ToString().ShouldNotContain("SENTINELA");
    }

    [Fact]
    public void Empty_ReportsFalse()
    {
        var check = Run(string.Empty, string.Empty, string.Empty);

        check.ClientIdConfigured.ShouldBeFalse();
        check.ClientSecretConfigured.ShouldBeFalse();
        check.RedirectUri.ShouldBe(string.Empty);
    }
}

public class RunCalendarDebugTests
{
    private const string Token = "TOKEN-SENTINELA-abcdefghijklmnop";

    private readonly IGoogleAccountService _accounts = Substitute.For<IGoogleAccountService>();
    private readonly ICalendarClient _calendar = Substitute.For<ICalendarClient>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public RunCalendarDebugTests() => _clock.Today.Returns(new DateOnly(2026, 10, 4));

    private RunCalendarDebug UseCase() => new(_accounts, _calendar, _clock);

    private static CalendarEvent Event(string title) => new("id-sentinela", title, "2026-10-04T10:00:00-03:00", "2026-10-04T11:00:00-03:00", string.Empty, string.Empty, false, null);

    private void Connected(int calendars, params CalendarEvent[] events)
    {
        _accounts.IsConnectedAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _accounts.GetCredentialsAsync(1, Arg.Any<CancellationToken>())
            .Returns(new GoogleCredentialResult(GoogleCredentialStatus.Connected, Token, ["scope"]));
        _calendar.ListSelectedCalendarsAsync(1, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, calendars).Select(i => new CalendarInfo("cal" + i, "Cal " + i, i == 0)).ToList());
        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(events);
    }

    [Fact]
    public async Task Connected_RunsTheFiveSteps_WithoutTheToken()
    {
        Connected(2, Event("Reunião"), Event("Dentista"));

        var report = await UseCase().ExecuteAsync(CancellationToken.None);

        report.Connected.ShouldBeTrue();
        report.Html.ShouldBe(
            "<pre>" + string.Join(
                "\n",
                "============================================================",
                "🧪 TESTE DE DEBUG - GOOGLE CALENDAR",
                "============================================================",
                "\n[TESTE 1] Verificando conexão...",
                "✅ Usuário conectado? True",
                "\n[TESTE 2] Obtendo credenciais...",
                "✅ Credenciais obtidas",
                "   - Token: [oculto]",
                "\n[TESTE 3] Criando serviço Calendar...",
                "✅ Serviço criado",
                "\n[TESTE 4] Listando calendários...",
                "✅ 2 calendários encontrados",
                "\n[TESTE 5] Buscando eventos de HOJE...",
                "   Data: 2026-10-04",
                "✅ API retornou 2 eventos",
                "\n   Eventos encontrados:",
                "   1. Reunião",
                "   2. Dentista",
                "\n✅ TODOS OS TESTES PASSARAM!") + "</pre>");
        report.Html.ShouldNotContain("SENTINELA");
        report.Html.ShouldNotContain("Expiry");
    }

    [Fact]
    public async Task Connected_ListsAtMostThreeEvents_AndEscapesTitles()
    {
        Connected(1, Event("<b>x</b>"), Event("B"), Event("C"), Event("D"));

        var report = await UseCase().ExecuteAsync(CancellationToken.None);

        report.Html.ShouldContain("API retornou 4 eventos");
        report.Html.ShouldContain("   1. &lt;b&gt;x&lt;/b&gt;");
        report.Html.ShouldContain("   3. C");
        report.Html.ShouldNotContain("   4. D");
        report.Html.ShouldNotContain("<b>");
    }

    [Fact]
    public async Task Connected_WithoutEvents_OmitsTheEventList()
    {
        Connected(1);

        var report = await UseCase().ExecuteAsync(CancellationToken.None);

        report.Html.ShouldContain("✅ API retornou 0 eventos");
        report.Html.ShouldNotContain("Eventos encontrados");
    }

    [Fact]
    public async Task Connected_AsksForTodayInSaoPauloAndForUserOne()
    {
        Connected(1);

        await UseCase().ExecuteAsync(CancellationToken.None);

        await _calendar.Received(1).GetEventsInRangeAsync(1, new DateOnly(2026, 10, 4), new DateOnly(2026, 10, 4), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotConnected_StopsAfterStepOne_WithBrAndWithoutPre()
    {
        _accounts.IsConnectedAsync(1, Arg.Any<CancellationToken>()).Returns(false);

        var report = await UseCase().ExecuteAsync(CancellationToken.None);

        report.Connected.ShouldBeFalse();
        report.Html.ShouldBe(
            "============================================================<br>🧪 TESTE DE DEBUG - GOOGLE CALENDAR<br>" +
            "============================================================<br>\n[TESTE 1] Verificando conexão...<br>" +
            "✅ Usuário conectado? False<br>❌ Usuário não conectado!");
        report.Html.ShouldNotContain("<pre>");
        await _accounts.DidNotReceive().GetCredentialsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _calendar.DidNotReceiveWithAnyArgs().ListSelectedCalendarsAsync(default, default);
    }

    [Fact]
    public async Task CredentialsNotConnected_SkipsTheCredentialLines_LikeFlask()
    {
        Connected(1);
        _accounts.GetCredentialsAsync(1, Arg.Any<CancellationToken>()).Returns(GoogleCredentialResult.Of(GoogleCredentialStatus.NeedsReconnect));

        var report = await UseCase().ExecuteAsync(CancellationToken.None);

        report.Html.ShouldNotContain("Credenciais obtidas");
        report.Html.ShouldNotContain("Token");
        report.Html.ShouldContain("✅ TODOS OS TESTES PASSARAM!");
    }

    [Fact]
    public async Task CalendarFailure_Propagates_ForTheEndpointToTurnInto500()
    {
        Connected(1);
        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CalendarEvent>>(_ => throw new CalendarApiException("falha", null));

        await Should.ThrowAsync<CalendarApiException>(() => UseCase().ExecuteAsync(CancellationToken.None));
    }
}
