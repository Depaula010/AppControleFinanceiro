using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Google;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MeuSecretario.UnitTests.Infrastructure.Google;

/// <summary>Calendar com o fake do Google: valida o que sai (URL, método, query, corpo) e o que volta (spec 010d, T34 e T35).</summary>
public sealed class CalendarClientTests : IDisposable
{
    private const string Base = "https://www.googleapis.com/calendar/v3";

    private readonly FakeHttpHandler _handler = new();
    private readonly IGoogleAccountService _account = Substitute.For<IGoogleAccountService>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero)); // 09:00 em São Paulo
    private readonly CalendarClient _client;

    public CalendarClientTests()
    {
        _account.GetCredentialsAsync(7, Arg.Any<CancellationToken>()).Returns(
            new GoogleCredentialResult(GoogleCredentialStatus.Connected, "tok-1", ["https://www.googleapis.com/auth/calendar"]));
        _client = new CalendarClient(_account, new FakeGoogleHttpClientFactory(_handler), new BrazilClock(_time), NullLogger<CalendarClient>.Instance);
    }

    public void Dispose() => _handler.Dispose();

    private static System.Collections.Specialized.NameValueCollection Query(RecordedRequest request) =>
        HttpUtility.ParseQueryString(request.Uri.Query);

    private static string Path(RecordedRequest request) => Uri.UnescapeDataString(request.Uri.AbsolutePath);

    private static void ShouldMatchJson(string? actual, string expected)
    {
        actual.ShouldNotBeNull();
        JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected)).ShouldBeTrue($"esperado {expected}, veio {actual}");
    }

    private const string TwoCalendars =
        """
        {"items":[
          {"id":"primary@x.com","summary":"Pessoal","selected":true,"primary":true},
          {"id":"trabalho@x.com","selected":true},
          {"id":"feriados@x.com","summary":"Feriados","selected":false},
          {"id":"semflag@x.com","summary":"Sem flag"}
        ]}
        """;

    // ============================== conexão (AC-010-98) ==============================

    [Theory]
    [InlineData(GoogleCredentialStatus.NotConnected)]
    [InlineData(GoogleCredentialStatus.NeedsReconnect)]
    [InlineData(GoogleCredentialStatus.Unavailable)]
    [Trait("AC", "010-98")]
    public async Task WithoutCredentials_ThrowsNotConnected_WithTheExactText_AndNeverCallsGoogle(GoogleCredentialStatus status)
    {
        _account.GetCredentialsAsync(8, Arg.Any<CancellationToken>()).Returns(GoogleCredentialResult.Of(status));

        var ex = await Should.ThrowAsync<GoogleNotConnectedException>(() => _client.ListSelectedCalendarsAsync(8, CancellationToken.None));

        ex.Message.ShouldBe("Usuário não conectou Google Calendar");
        _handler.Requests.ShouldBeEmpty();
    }

    // ============================== listar (AC-010-90, 91) ==============================

    [Fact]
    [Trait("AC", "010-91")]
    public async Task ListSelectedCalendars_FiltersSelected_WithDefaultsAndBearerToken()
    {
        _handler.Enqueue(HttpStatusCode.OK, TwoCalendars);

        var calendars = await _client.ListSelectedCalendarsAsync(7, CancellationToken.None);

        calendars.ShouldBe([new CalendarInfo("primary@x.com", "Pessoal", true), new CalendarInfo("trabalho@x.com", "Sem nome", false)]);
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.GetLeftPart(UriPartial.Path).ShouldBe($"{Base}/users/me/calendarList");
        request.Headers["Authorization"].ShouldBe("Bearer tok-1");
    }

    [Fact]
    [Trait("AC", "010-90")]
    public async Task ListEvents_SendsRfc3339WithMinusThree_SingleEventsAndOrderBy()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"items":[]}""");
        var min = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.FromHours(-3));
        var max = new DateTimeOffset(2026, 10, 2, 23, 59, 59, TimeSpan.FromHours(-3)).AddTicks(9_999_990);

        await _client.ListEventsAsync(7, new ListEventsQuery("primary", min, max), CancellationToken.None);

        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Uri.GetLeftPart(UriPartial.Path).ShouldBe($"{Base}/calendars/primary/events");
        var query = Query(request);
        query["timeMin"].ShouldBe("2026-10-02T00:00:00-03:00");
        query["timeMax"].ShouldBe("2026-10-02T23:59:59.999999-03:00");
        query["singleEvents"].ShouldBe("true");
        query["orderBy"].ShouldBe("startTime");
        query["q"].ShouldBeNull();
        query["maxResults"].ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-90")]
    public async Task ListEvents_WithRawTextsQueryAndMaxResults_SendsThemToo()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"items":[]}""");

        await _client.ListEventsAsync(
            7,
            new ListEventsQuery("abc@group.calendar.google.com", null, null, "2026-10-02T00:00:00-03:00", "2026-10-03T00:00:00-03:00", "dentista", 10),
            CancellationToken.None);

        var request = _handler.Requests.ShouldHaveSingleItem();
        Path(request).ShouldBe("/calendar/v3/calendars/abc@group.calendar.google.com/events");
        var query = Query(request);
        query["timeMin"].ShouldBe("2026-10-02T00:00:00-03:00");
        query["timeMax"].ShouldBe("2026-10-03T00:00:00-03:00");
        query["q"].ShouldBe("dentista");
        query["maxResults"].ShouldBe("10");
    }

    [Fact]
    public async Task ListEvents_MapsEventsWithDefaults()
    {
        _handler.Enqueue(
            HttpStatusCode.OK,
            """
            {"items":[
              {"id":"e1","summary":"Reuniao","start":{"dateTime":"2026-10-02T10:00:00-03:00"},"end":{"dateTime":"2026-10-02T11:00:00-03:00"},"location":"Sala","description":"d"},
              {"id":"e2","start":{"date":"2026-10-02"},"end":{"date":"2026-10-03"}}
            ]}
            """);

        var events = await _client.ListEventsAsync(7, new ListEventsQuery("primary", null, null), CancellationToken.None);

        events.ShouldBe(
        [
            new CalendarEvent("e1", "Reuniao", "2026-10-02T10:00:00-03:00", "2026-10-02T11:00:00-03:00", "Sala", "d", false, null, "primary"),
            new CalendarEvent("e2", "Sem título", "2026-10-02", "2026-10-03", string.Empty, string.Empty, true, null, "primary"),
        ]);
    }

    [Fact]
    [Trait("AC", "010-90")]
    public async Task ListEvents_ApiError_BecomesCalendarApiException_WithoutBodyOrToken()
    {
        _handler.Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"segredo tok-1","errors":[{"reason":"forbidden"}]}}""");

        var ex = await Should.ThrowAsync<CalendarApiException>(() =>
            _client.ListEventsAsync(7, new ListEventsQuery("primary", null, null), CancellationToken.None));

        ex.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ex.Message.ShouldNotContain("tok-1");
        ex.Message.ShouldNotContain("segredo");
    }

    [Fact]
    [Trait("AC", "010-91")]
    public async Task GetEventsInRange_QueriesEverySelectedCalendar_SortsByStart_AndTagsTheCalendar()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, TwoCalendars)
            .Enqueue(HttpStatusCode.OK, """{"items":[{"id":"b","summary":"Tarde","start":{"dateTime":"2026-10-02T15:00:00-03:00"},"end":{"dateTime":"2026-10-02T16:00:00-03:00"}}]}""")
            .Enqueue(HttpStatusCode.OK, """{"items":[{"id":"a","summary":"Manha","start":{"dateTime":"2026-10-02T09:00:00-03:00"},"end":{"dateTime":"2026-10-02T10:00:00-03:00"}},{"id":"c","summary":"Dia","start":{"date":"2026-10-02"},"end":{"date":"2026-10-03"}}]}""");

        var events = await _client.GetEventsInRangeAsync(7, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2), CancellationToken.None);

        events.Select(e => e.Id).ShouldBe(["c", "a", "b"]); // "2026-10-02" < "2026-10-02T09..." < "...T15..."
        events.Single(e => e.Id == "b").CalendarName.ShouldBe("Pessoal");
        events.Single(e => e.Id == "a").CalendarName.ShouldBe("Sem nome");
        events.Single(e => e.Id == "c").AllDay.ShouldBeTrue();

        var requests = _handler.Requests;
        requests.Count.ShouldBe(3); // só os 2 selecionados
        Path(requests[1]).ShouldBe("/calendar/v3/calendars/primary@x.com/events");
        Path(requests[2]).ShouldBe("/calendar/v3/calendars/trabalho@x.com/events");
        var query = Query(requests[1]);
        query["timeMin"].ShouldBe("2026-10-02T00:00:00-03:00");
        query["timeMax"].ShouldBe("2026-10-02T23:59:59.999999-03:00");
        query["singleEvents"].ShouldBe("true");
        query["orderBy"].ShouldBe("startTime");
    }

    [Fact]
    [Trait("AC", "010-91")]
    public async Task GetEventsInRange_WhenCalendarListFails_QueriesOnlyPrimary()
    {
        _handler
            .Enqueue(HttpStatusCode.InternalServerError, "{}")
            .Enqueue(HttpStatusCode.OK, """{"items":[{"id":"p","summary":"X","start":{"dateTime":"2026-10-02T09:00:00-03:00"},"end":{"dateTime":"2026-10-02T10:00:00-03:00"}}]}""");

        var events = await _client.GetEventsInRangeAsync(7, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 4), CancellationToken.None);

        events.ShouldHaveSingleItem().CalendarName.ShouldBe("Primary");
        var requests = _handler.Requests;
        requests.Count.ShouldBe(2);
        Path(requests[1]).ShouldBe("/calendar/v3/calendars/primary/events");
        Query(requests[1])["timeMax"].ShouldBe("2026-10-04T23:59:59.999999-03:00");
    }

    [Fact]
    [Trait("AC", "010-91")]
    public async Task GetEventsInRange_SkipsACalendarThatFails_AndKeepsTheOther()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, TwoCalendars)
            .Enqueue(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"x"}}""")
            .Enqueue(HttpStatusCode.OK, """{"items":[{"id":"ok","summary":"Vale","start":{"dateTime":"2026-10-02T09:00:00-03:00"},"end":{"dateTime":"2026-10-02T10:00:00-03:00"}}]}""");

        var events = await _client.GetEventsInRangeAsync(7, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2), CancellationToken.None);

        events.ShouldHaveSingleItem().Id.ShouldBe("ok");
    }

    [Fact]
    [Trait("AC", "010-91")]
    public async Task GetEventsInRange_UsesTheHistoricalOffsetOfEachDate()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"items":[{"id":"p","selected":true}]}""").Enqueue(HttpStatusCode.OK, """{"items":[]}""");

        await _client.GetEventsInRangeAsync(7, new DateOnly(2018, 12, 1), new DateOnly(2018, 12, 1), CancellationToken.None);

        Query(_handler.Requests[1])["timeMin"].ShouldBe("2018-12-01T00:00:00-02:00"); // horário de verão de 2018
    }

    [Fact]
    public async Task GetEvent_Returns404AsNull()
    {
        _handler.Enqueue(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"Not Found"}}""");

        (await _client.GetEventAsync(7, "x", "primary", CancellationToken.None)).ShouldBeNull();
    }

    // ============================== criar (AC-010-92) ==============================

    [Fact]
    [Trait("AC", "010-92")]
    public async Task Insert_AllDay_SendsTheExactBody()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"id":"novo-1","htmlLink":"https://x"}""");

        var id = await _client.InsertEventAsync(7, new NewCalendarEvent("Aniversario", new DateOnly(2025, 11, 20), null, null, null, null), CancellationToken.None);

        id.ShouldBe("novo-1");
        var request = _handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        Path(request).ShouldBe("/calendar/v3/calendars/primary/events");
        ShouldMatchJson(
            request.BodyText,
            """{"summary":"Aniversario","start":{"date":"2025-11-20","timeZone":"America/Sao_Paulo"},"end":{"date":"2025-11-21","timeZone":"America/Sao_Paulo"}}""");
    }

    [Fact]
    [Trait("AC", "010-92")]
    public async Task Insert_WithStart_SendsDescriptionLocationAndIsoDateTimes()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"id":"novo-2"}""");

        await _client.InsertEventAsync(
            7,
            new NewCalendarEvent("Dentista", new DateOnly(2025, 11, 20), new TimeOnly(7, 0), null, "Limpeza", "Clinica Sorriso", "outro@x.com"),
            CancellationToken.None);

        var request = _handler.Requests.ShouldHaveSingleItem();
        Path(request).ShouldBe("/calendar/v3/calendars/outro@x.com/events");
        ShouldMatchJson(
            request.BodyText,
            """
            {"summary":"Dentista","description":"Limpeza","location":"Clinica Sorriso",
             "start":{"dateTime":"2025-11-20T07:00:00-03:00","timeZone":"America/Sao_Paulo"},
             "end":{"dateTime":"2025-11-20T08:00:00-03:00","timeZone":"America/Sao_Paulo"}}
            """);
    }

    [Fact]
    [Trait("AC", "010-92")]
    public async Task Insert_WithStartAndEnd_SendsBoth()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"id":"novo-3"}""");

        await _client.InsertEventAsync(
            7, new NewCalendarEvent("Reuniao", new DateOnly(2025, 11, 20), new TimeOnly(9, 30), new TimeOnly(11, 0), null, null), CancellationToken.None);

        ShouldMatchJson(
            _handler.Requests.ShouldHaveSingleItem().BodyText,
            """
            {"summary":"Reuniao",
             "start":{"dateTime":"2025-11-20T09:30:00-03:00","timeZone":"America/Sao_Paulo"},
             "end":{"dateTime":"2025-11-20T11:00:00-03:00","timeZone":"America/Sao_Paulo"}}
            """);
    }

    [Fact]
    [Trait("AC", "010-92")]
    public async Task Insert_ApiError_ThrowsCalendarApiException()
    {
        _handler.Enqueue(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"Bad"}}""");

        var ex = await Should.ThrowAsync<CalendarApiException>(() =>
            _client.InsertEventAsync(7, new NewCalendarEvent("x", new DateOnly(2025, 11, 20), null, null, null, null), CancellationToken.None));

        ex.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ============================== atualizar e remover (AC-010-93) ==============================

    private const string CurrentEvent =
        """
        {"id":"ev1","summary":"Antigo","description":"d","location":"L","status":"confirmed",
         "start":{"dateTime":"2025-11-20T10:00:00-03:00","timeZone":"America/Sao_Paulo"},
         "end":{"dateTime":"2025-11-20T11:30:00-03:00","timeZone":"America/Sao_Paulo"}}
        """;

    [Fact]
    [Trait("AC", "010-93")]
    public async Task Update_TitleOnly_KeepsStartAndEnd_AndSendsTheWholeEventWithPut()
    {
        _handler.Enqueue(HttpStatusCode.OK, CurrentEvent).Enqueue(HttpStatusCode.OK, CurrentEvent);

        var found = await _client.UpdateEventAsync(7, "ev1", "primary", new EventUpdate("Novo", null, null, null, null, null), CancellationToken.None);

        found.ShouldBeTrue();
        var requests = _handler.Requests;
        requests[0].Method.ShouldBe(HttpMethod.Get);
        Path(requests[0]).ShouldBe("/calendar/v3/calendars/primary/events/ev1");
        requests[1].Method.ShouldBe(HttpMethod.Put);
        Path(requests[1]).ShouldBe("/calendar/v3/calendars/primary/events/ev1");
        var body = JsonNode.Parse(requests[1].BodyText!)!;
        body["summary"]!.GetValue<string>().ShouldBe("Novo");
        body["description"]!.GetValue<string>().ShouldBe("d");
        body["status"]!.GetValue<string>().ShouldBe("confirmed");
        body["start"]!["dateTime"]!.GetValue<string>().ShouldBe("2025-11-20T10:00:00-03:00");
        body["end"]!["dateTime"]!.GetValue<string>().ShouldBe("2025-11-20T11:30:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public async Task Update_WithStartOnly_KeepsTheDuration()
    {
        _handler.Enqueue(HttpStatusCode.OK, CurrentEvent).Enqueue(HttpStatusCode.OK, CurrentEvent);

        await _client.UpdateEventAsync(7, "ev1", "primary", new EventUpdate(null, null, new TimeOnly(14, 0), null, null, null), CancellationToken.None);

        var body = JsonNode.Parse(_handler.Requests[1].BodyText!)!;
        body["start"]!["dateTime"]!.GetValue<string>().ShouldBe("2025-11-20T14:00:00-03:00");
        body["end"]!["dateTime"]!.GetValue<string>().ShouldBe("2025-11-20T15:30:00-03:00");
        body["start"]!["timeZone"]!.GetValue<string>().ShouldBe("America/Sao_Paulo");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public async Task Update_EmptyDescriptionClearsIt_AndNullKeepsIt()
    {
        _handler.Enqueue(HttpStatusCode.OK, CurrentEvent).Enqueue(HttpStatusCode.OK, CurrentEvent);

        await _client.UpdateEventAsync(7, "ev1", "primary", new EventUpdate(null, null, null, null, string.Empty, null), CancellationToken.None);

        var body = JsonNode.Parse(_handler.Requests[1].BodyText!)!;
        (body["description"]?.GetValue<string>() ?? string.Empty).ShouldBeEmpty();
        body["location"]!.GetValue<string>().ShouldBe("L");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public async Task Update_MissingEvent_ReturnsFalse()
    {
        _handler.Enqueue(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"Not Found"}}""");

        (await _client.UpdateEventAsync(7, "ev1", "primary", new EventUpdate("x", null, null, null, null, null), CancellationToken.None)).ShouldBeFalse();
        _handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-93")]
    public async Task Delete_ReadsTheTitleThenDeletes()
    {
        _handler.Enqueue(HttpStatusCode.OK, CurrentEvent).Enqueue(HttpStatusCode.NoContent);

        var result = await _client.DeleteEventAsync(7, "ev1", "primary", CancellationToken.None);

        result.ShouldBe(new CalendarDeleteResult(true, "Antigo"));
        var requests = _handler.Requests;
        requests[1].Method.ShouldBe(HttpMethod.Delete);
        Path(requests[1]).ShouldBe("/calendar/v3/calendars/primary/events/ev1");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public async Task Delete_MissingEvent_IsNotFound_WithTheDefaultTitle()
    {
        _handler
            .Enqueue(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"Not Found"}}""")
            .Enqueue(HttpStatusCode.NotFound, """{"error":{"code":404,"message":"Not Found"}}""");

        var result = await _client.DeleteEventAsync(7, "ev1", "primary", CancellationToken.None);

        result.ShouldBe(new CalendarDeleteResult(false, "Evento"));
    }

    [Fact]
    [Trait("AC", "010-93")]
    public async Task Delete_EventWithoutTitle_UsesSemTitulo_AndOtherErrorsThrow()
    {
        _handler.Enqueue(HttpStatusCode.OK, """{"id":"ev1","start":{"date":"2025-11-20"},"end":{"date":"2025-11-21"}}""")
            .Enqueue(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"x"}}""");

        var ex = await Should.ThrowAsync<CalendarApiException>(() => _client.DeleteEventAsync(7, "ev1", "primary", CancellationToken.None));
        ex.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        _handler.Enqueue(HttpStatusCode.OK, """{"id":"ev1","start":{"date":"2025-11-20"},"end":{"date":"2025-11-21"}}""").Enqueue(HttpStatusCode.NoContent);
        (await _client.DeleteEventAsync(7, "ev1", "primary", CancellationToken.None)).Title.ShouldBe("Sem título");
    }

    // ============================== busca por título (AC-010-90) ==============================

    [Fact]
    [Trait("AC", "010-90")]
    public async Task FindByTitle_QueriesEachCalendarWithQ_MaxResults_AndA90DayWindow()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, TwoCalendars)
            .Enqueue(HttpStatusCode.OK, """{"items":[{"id":"e1","summary":"Dentista","start":{"dateTime":"2026-10-10T10:00:00-03:00"},"end":{"dateTime":"2026-10-10T11:00:00-03:00"}}]}""")
            .Enqueue(HttpStatusCode.OK, """{"items":[]}""");

        var found = await _client.FindEventsByTitleAsync(7, "dentista", CancellationToken.None);

        var item = found.ShouldHaveSingleItem();
        item.Id.ShouldBe("e1");
        item.CalendarId.ShouldBe("primary@x.com");
        item.CalendarName.ShouldBe("Pessoal");
        item.Start.ShouldBe("2026-10-10T10:00:00-03:00");
        var requests = _handler.Requests;
        requests.Count.ShouldBe(3);
        var query = Query(requests[1]);
        query["q"].ShouldBe("dentista");
        query["maxResults"].ShouldBe("10");
        query["singleEvents"].ShouldBe("true");
        query["orderBy"].ShouldBe("startTime");
        query["timeMin"].ShouldBe("2026-10-02T09:00:00-03:00");
        query["timeMax"].ShouldBe("2026-12-31T09:00:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-90")]
    public async Task FindByTitle_CalendarErrorIsSkipped_AndNotConnectedYieldsEmpty()
    {
        _handler
            .Enqueue(HttpStatusCode.OK, TwoCalendars)
            .Enqueue(HttpStatusCode.InternalServerError, "{}")
            .Enqueue(HttpStatusCode.OK, """{"items":[{"id":"e2","summary":"Dentista","start":{"date":"2026-10-10"},"end":{"date":"2026-10-11"}}]}""");

        (await _client.FindEventsByTitleAsync(7, "dentista", CancellationToken.None)).ShouldHaveSingleItem().Id.ShouldBe("e2");

        _account.GetCredentialsAsync(9, Arg.Any<CancellationToken>()).Returns(GoogleCredentialResult.Of(GoogleCredentialStatus.NotConnected));
        (await _client.FindEventsByTitleAsync(9, "x", CancellationToken.None)).ShouldBeEmpty();
    }
}
