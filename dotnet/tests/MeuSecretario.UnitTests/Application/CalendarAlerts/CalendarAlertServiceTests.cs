using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Application.Caching;
using MeuSecretario.Domain.CalendarAlerts;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.CalendarAlerts;

public class CalendarAlertServiceTests
{
    private const int UserId = 7;
    private const string Number = "5511999990000";

    // 17:59:20 em São Paulo (UTC-3) = 20:59:20 UTC.
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 20, 59, 20, TimeSpan.Zero);

    private readonly ICalendarClient _calendar = Substitute.For<ICalendarClient>();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryCache _cache;
    private readonly IBotClient _bot = Substitute.For<IBotClient>();
    private readonly ICalendarAlertDiagnostics _diagnostics = Substitute.For<ICalendarAlertDiagnostics>();
    private readonly CalendarAlertService _service;

    public CalendarAlertServiceTests()
    {
        _cache = new InMemoryCache(_time);
        _service = new CalendarAlertService(_calendar, _cache, _bot, new BrazilClock(_time), _diagnostics, TimeSpan.Zero);
        _calendar.ListSelectedCalendarsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new CalendarInfo("primary", "Pessoal", true)]);
        _bot.SendTextAsync(Number, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    private static CalendarEvent Timed(string id, string title, string start, string location = "", bool allDay = false) =>
        new(id, title, start, start, location, string.Empty, allDay, null);

    private void Events(string calendarId, params CalendarEvent[] events) =>
        _calendar.ListEventsAsync(UserId, Arg.Is<ListEventsQuery>(q => q.CalendarId == calendarId), Arg.Any<CancellationToken>())
            .Returns(events);

    // ---------------------------------------------------------------- janela e filtro

    [Fact]
    [Trait("AC", "014-40")]
    public async Task Upcoming_AsksGoogleForTheWindowFromFourMinutesBeforeToThirtySecondsAfterTheTarget_InSaoPauloOffset()
    {
        Events("primary");

        await _service.GetUpcomingEventsAsync(UserId, 1, CancellationToken.None);

        // Alvo 18:00:20; limite inferior = alvo − 60 s − 3 min de tolerância (desvio 123).
        var expectedStart = new DateTimeOffset(2026, 10, 5, 17, 56, 20, TimeSpan.FromHours(-3));
        var expectedEnd = new DateTimeOffset(2026, 10, 5, 18, 0, 50, TimeSpan.FromHours(-3));
        await _calendar.Received(1).ListEventsAsync(
            UserId,
            Arg.Is<ListEventsQuery>(q => q.TimeMin == expectedStart && q.TimeMax == expectedEnd && q.TimeMin!.Value.Offset == TimeSpan.FromHours(-3)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "014-41")]
    public async Task Upcoming_KeepsTimedFutureEvents_AndDropsAllDayAndPastOnes()
    {
        Events(
            "primary",
            Timed("a", "Reunião", "2026-10-05T18:00:00-03:00", "Sala 2"),
            Timed("b", "Feriado", "2026-10-05", allDay: true),
            Timed("c", "Já começou", "2026-10-05T17:59:10-03:00"),
            Timed("d", "Agora exato", "2026-10-05T17:59:20-03:00"),
            Timed("e", "Sem hora legível", "texto"));

        var events = await _service.GetUpcomingEventsAsync(UserId, 1, CancellationToken.None);

        events.ShouldBe([new UpcomingEvent("a", "Reunião", "2026-10-05T18:00:00-03:00", "Sala 2", string.Empty, "Pessoal")]);
    }

    [Fact]
    [Trait("AC", "014-48")]
    public async Task Upcoming_SeesTheEventTheLastCycleMissed_AndDropsLongEventsStartedBeforeTheWindow()
    {
        // Desvio 121: alvo 18:00:20. 17:59:21 é do ciclo anterior (perdido); 18:00:50 é a fronteira inclusiva.
        // O Google filtra pelo FIM do evento: um evento longo que começou às 17:30 também volta e é descartado.
        Events(
            "primary",
            Timed("perdido", "Perdido", "2026-10-05T17:59:21-03:00"),
            Timed("borda", "Borda", "2026-10-05T18:00:50-03:00"),
            Timed("fora", "Fora", "2026-10-05T18:00:51-03:00"),
            Timed("longo", "Longo", "2026-10-05T17:59:20-03:00"));

        var events = await _service.GetUpcomingEventsAsync(UserId, 1, CancellationToken.None);

        events.Select(e => e.Id).ShouldBe(["perdido", "borda"]);
    }

    [Fact]
    [Trait("AC", "014-48")]
    public async Task Process_TheEventMissedByTheLastCycle_IsAlertedOnce_ThenTheClaimBlocksIt()
    {
        Events("primary", Timed("perdido", "Perdido", "2026-10-05T17:59:21-03:00"));

        var first = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);
        var second = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);

        first.ShouldBe(1);
        second.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "014-49")]
    public async Task Upcoming_Grace_KeepsTheEventForThreeMoreMinutes_BeforeItStarts()
    {
        // Desvio 123: 10 min antes, alvo 18:09:20. O limite inferior é 18:05:20 (exclusivo): 18:06:00 ainda entra.
        Events(
            "primary",
            Timed("tolerado", "Tolerado", "2026-10-05T18:06:00-03:00"),
            Timed("limite", "Limite", "2026-10-05T18:05:20-03:00"),
            Timed("antigo", "Antigo", "2026-10-05T18:05:00-03:00"));

        var events = await _service.GetUpcomingEventsAsync(UserId, 10, CancellationToken.None);

        events.Select(e => e.Id).ShouldBe(["tolerado"]);
    }

    [Fact]
    [Trait("AC", "014-41")]
    public async Task Upcoming_SumsSelectedCalendars_InOrder_WithTheCalendarName()
    {
        _calendar.ListSelectedCalendarsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new CalendarInfo("c1", "Trabalho", false), new CalendarInfo("c2", string.Empty, false)]);
        Events("c1", Timed("a", "Daily", "2026-10-05T18:00:00-03:00"));
        Events("c2", Timed("b", "Médico", "2026-10-05T18:00:10-03:00"));

        var events = await _service.GetUpcomingEventsAsync(UserId, 1, CancellationToken.None);

        events.Select(e => (e.Id, e.CalendarName)).ShouldBe([("a", "Trabalho"), ("b", "Sem nome")]);
    }

    [Fact]
    [Trait("AC", "014-42")]
    public async Task Upcoming_ACalendarFailing_DoesNotStopTheOthers()
    {
        _calendar.ListSelectedCalendarsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new CalendarInfo("ruim", "Ruim", false), new CalendarInfo("bom", "Bom", false)]);
        _calendar.ListEventsAsync(UserId, Arg.Is<ListEventsQuery>(q => q.CalendarId == "ruim"), Arg.Any<CancellationToken>())
            .ThrowsAsync(new CalendarApiException("falha", System.Net.HttpStatusCode.Forbidden));
        Events("bom", Timed("a", "Ok", "2026-10-05T18:00:00-03:00"));

        var events = await _service.GetUpcomingEventsAsync(UserId, 1, CancellationToken.None);

        events.Select(e => e.Id).ShouldBe(["a"]);
        _diagnostics.Received(1).CalendarEventsFailed(UserId, Arg.Any<CalendarApiException>());
    }

    [Fact]
    [Trait("AC", "014-42")]
    public async Task Upcoming_UserWithoutGoogle_OrAnyFailure_ReturnsEmpty_WithoutThrowing()
    {
        _calendar.ListSelectedCalendarsAsync(UserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new GoogleNotConnectedException("Usuário não conectou Google Calendar"));

        (await _service.GetUpcomingEventsAsync(UserId, 1, CancellationToken.None)).ShouldBeEmpty();

        _calendar.ListSelectedCalendarsAsync(UserId, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));

        (await _service.GetUpcomingEventsAsync(UserId, 1, CancellationToken.None)).ShouldBeEmpty();
        _diagnostics.Received(2).EventsLookupFailed(UserId, Arg.Any<Exception>());
    }

    [Fact]
    [Trait("AC", "014-42")]
    public async Task Upcoming_Cancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        _calendar.ListSelectedCalendarsAsync(UserId, Arg.Any<CancellationToken>()).ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => _service.GetUpcomingEventsAsync(UserId, 1, cts.Token));
    }

    // ---------------------------------------------------------------- claim e envio

    [Fact]
    [Trait("AC", "014-43")]
    public async Task Process_SendsOnce_ClaimsWithTheExactKeyValueAndTtl_AndSecondCycleDoesNotResend()
    {
        Events("primary", Timed("a", "Reunião", "2026-10-05T18:00:00-03:00", "Sala 2"));

        var first = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);
        var second = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);

        first.ShouldBe(1);
        second.ShouldBe(0);
        await _bot.Received(1).SendTextAsync(Number, "⚠️ Faltam 1 min: Reunião\n🕒 18:00\n📍 Sala 2", Arg.Any<CancellationToken>());
        var key = CacheKeys.CalendarAlert(UserId, "a", "2026-10-05T18:00:00-03:00");
        _cache.RawValue(key).ShouldBe("processing");
        _cache.TtlOf(key).ShouldBe(TimeSpan.FromSeconds(7200));
    }

    [Fact]
    [Trait("AC", "014-44")]
    public async Task Process_WithRedisDown_SendsWithoutLock()
    {
        _cache.Unavailable = true;
        Events("primary", Timed("a", "Reunião", "2026-10-05T18:00:00-03:00"));

        var sent = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);

        sent.ShouldBe(1);
        _diagnostics.Received(1).RedisUnavailable(UserId);
    }

    [Fact]
    [Trait("AC", "014-44")]
    public async Task Process_FailedSend_ReleasesTheClaim_SoTheNextCycleRetries()
    {
        Events("primary", Timed("a", "Reunião", "2026-10-05T18:00:00-03:00"));
        // Duas falhas no ciclo (envio e nova tentativa); o ciclo seguinte envia.
        _bot.SendTextAsync(Number, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false, false, true);

        var first = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);
        _cache.RawValue(CacheKeys.CalendarAlert(UserId, "a", "2026-10-05T18:00:00-03:00")).ShouldBeNull();
        var second = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);

        first.ShouldBe(0);
        second.ShouldBe(1);
        await _bot.Received(3).SendTextAsync(Number, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _diagnostics.Received(1).AlertCycle(UserId, 1, 0, 1);
        _diagnostics.Received(1).AlertCycle(UserId, 1, 1, 0);
    }

    [Fact]
    [Trait("AC", "014-49")]
    public async Task Process_FailedSend_IsRetriedOnceInTheSameCycle()
    {
        Events("primary", Timed("a", "Reunião", "2026-10-05T18:00:00-03:00"));
        _bot.SendTextAsync(Number, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false, true);

        var sent = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);

        sent.ShouldBe(1);
        await _bot.Received(2).SendTextAsync(Number, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _cache.RawValue(CacheKeys.CalendarAlert(UserId, "a", "2026-10-05T18:00:00-03:00")).ShouldBe("processing");
        _diagnostics.Received(1).AlertCycle(UserId, 1, 1, 0);
    }

    [Fact]
    [Trait("AC", "014-49")]
    public async Task Process_LateEvent_AnnouncesTheMinutesThatReallyRemain()
    {
        // 10 min antes, mas o evento (18:06:00) está a 6 min 40 s: o texto diz 7 min, não 10.
        Events("primary", Timed("a", "Reunião", "2026-10-05T18:06:00-03:00"));

        (await _service.ProcessAlertsForUserAsync(UserId, Number, 10, CancellationToken.None)).ShouldBe(1);

        await _bot.Received(1).SendTextAsync(Number, "⚠️ Faltam 7 min: Reunião\n🕒 18:06", Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "014-46")]
    public async Task Process_BotThrowing_OnOneEvent_ReleasesItsClaim_AndContinuesWithTheNext()
    {
        Events(
            "primary",
            Timed("a", "Primeiro", "2026-10-05T18:00:00-03:00"),
            Timed("b", "Segundo", "2026-10-05T18:00:10-03:00"));
        _bot.SendTextAsync(Number, Arg.Is<string>(m => m.Contains("Primeiro", StringComparison.Ordinal)), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("rede"));

        var sent = await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None);

        sent.ShouldBe(1);
        _cache.RawValue(CacheKeys.CalendarAlert(UserId, "a", "2026-10-05T18:00:00-03:00")).ShouldBeNull();
        _cache.RawValue(CacheKeys.CalendarAlert(UserId, "b", "2026-10-05T18:00:10-03:00")).ShouldBe("processing");
        _diagnostics.Received(2).AlertSendFailed(UserId, Arg.Any<HttpRequestException>());
    }

    [Fact]
    [Trait("AC", "014-46")]
    public async Task Process_NoEvents_ReturnsZero_AndSendsNothing()
    {
        Events("primary");

        (await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None)).ShouldBe(0);

        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "014-46")]
    public async Task Process_EventAlreadyClaimed_IsSkipped()
    {
        Events("primary", Timed("a", "Reunião", "2026-10-05T18:00:00-03:00"));
        await _cache.SetIfNotExistsAsync(
            CacheKeys.CalendarAlert(UserId, "a", "2026-10-05T18:00:00-03:00"), "processing", TimeSpan.FromHours(2));

        (await _service.ProcessAlertsForUserAsync(UserId, Number, 1, CancellationToken.None)).ShouldBe(0);

        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "014-46")]
    public async Task Send_ReturnsFalse_WhenTheBotThrows()
    {
        _bot.SendTextAsync(Number, Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("x"));
        var upcoming = new UpcomingEvent("a", "T", "2026-10-05T18:00:00-03:00", string.Empty, string.Empty, "C");

        (await _service.SendEventAlertAsync(Number, upcoming, 1, UserId, CancellationToken.None)).ShouldBeFalse();
    }
}
