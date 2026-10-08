using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Calendar;

/// <summary>012e: agenda e horários livres contra os serviços reais do Flask (fixtures com Google falso e relógio congelado).</summary>
public class CalendarServicesTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    [Fact]
    [Trait("AC", "012-505")]
    public void AgendaTimeFilter_MatchesTheFlask()
    {
        var events = CalendarRig.Cases("agenda_filter_events").Select(e => new CalendarEvent(
            e.GetProperty("summary").GetString()!, e.GetProperty("summary").GetString()!, e.GetProperty("start").GetString()!,
            e.GetProperty("end").GetString()!, string.Empty, string.Empty, e.GetProperty("all_day").GetBoolean(), null)).ToList();
        events.Count.ShouldBeGreaterThan(10);

        var cases = CalendarRig.Cases("agenda_filter_cases");
        cases.Count.ShouldBeGreaterThan(15);
        foreach (var c in cases)
        {
            var now = DateTimeOffset.Parse(c.GetProperty("now").GetString()!, CultureInfo.InvariantCulture);
            var local = TimeZoneInfo.ConvertTime(now, SaoPaulo).DateTime;
            var period = c.GetProperty("period").ValueKind == JsonValueKind.String ? c.GetProperty("period").GetString() : null;

            var kept = AgendaTimeFilter.Apply(events, period, local, SaoPaulo).Select(e => e.Summary);

            kept.ShouldBe(c.GetProperty("kept").EnumerateArray().Select(x => x.GetString()!), $"{c.GetProperty("now")} {period}");
        }
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task AgendaService_MatchesTheFlask_ForEveryScenario()
    {
        var world = WhatsAppFixture.Section("agenda_world");
        var cases = CalendarRig.Cases("agenda_cases");
        cases.Count.ShouldBeGreaterThan(25);

        foreach (var c in cases)
        {
            var name = c.GetProperty("name").GetString()!;
            var connected = c.GetProperty("connected").GetBoolean();
            var rig = new CalendarRig(c.GetProperty("now").GetString()!, world)
            {
                Client = { NotConnected = !connected, FailCalendarList = c.GetProperty("fail_list").GetBoolean() },
            };
            foreach (var failing in c.GetProperty("fail_events").EnumerateArray())
            {
                rig.Client.FailEventsFor.Add(failing.GetString()!);
            }

            var redirect = c.GetProperty("redirect").GetBoolean() ? "https://proxy.example/oauth/callback" : string.Empty;
            var service = new AgendaService(
                rig.Client, CalendarRig.Account(connected), CalendarRig.Links(redirect, rig.Time.GetUtcNow()), rig.Clock);

            var period = c.GetProperty("period").GetString();
            var filter = c.GetProperty("time_filter").ValueKind == JsonValueKind.String ? c.GetProperty("time_filter").GetString() : null;

            if (c.GetProperty("error").ValueKind == JsonValueKind.String)
            {
                var act = () => filter is null
                    ? service.QueryAsync(7, period, CancellationToken.None)
                    : service.QueryWithTimeFilterAsync(7, period, filter, CancellationToken.None);
                await Should.ThrowAsync<GoogleNotConnectedException>(act);
            }
            else
            {
                var text = filter is null
                    ? await service.QueryAsync(7, period, CancellationToken.None)
                    : await service.QueryWithTimeFilterAsync(7, period, filter, CancellationToken.None);
                CalendarRig.WithoutToken(text).ShouldBe(c.GetProperty("text").GetString(), name);
            }

            rig.Client.Calls.ShouldBe(c.GetProperty("calls").EnumerateArray().Select(CalendarRig.CallText), name);
        }
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task Agenda_WhenGoogleFailsMidway_AnswersTheErrorWithoutTheReason()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        var client = Substitute.For<ICalendarClient>();
        client.GetEventsInRangeAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CalendarEvent>>(_ => throw new InvalidOperationException("token ya29.SEGREDO invalido"));
        var service = new AgendaService(client, CalendarRig.Account(true), CalendarRig.Links("https://h/x", rig.Time.GetUtcNow()), rig.Clock);

        var text = await service.QueryAsync(7, "hoje", CancellationToken.None);

        text.ShouldBe("❌ Erro ao consultar agenda.\n\nTente reconectar seu Google Calendar.");
        text.ShouldNotContain("SEGREDO");
    }

    [Fact]
    [Trait("AC", "012-506")]
    public async Task FreeTime_MatchesTheFlask_ForEveryScenario()
    {
        var worlds = WhatsAppFixture.Section("free_worlds");
        var history = WhatsAppFixture.Section("free_history");
        var cases = CalendarRig.Cases("free_slots_cases");
        cases.Count.ShouldBeGreaterThan(15);

        foreach (var c in cases)
        {
            var name = c.GetProperty("name").GetString()!;
            var world = worlds.GetProperty(c.GetProperty("world").GetString()!);
            var hasHistory = c.GetProperty("history").ValueKind == JsonValueKind.Array;
            var rig = new CalendarRig(c.GetProperty("now").GetString()!, CalendarRig.WithHistory(world, hasHistory ? history : null))
            {
                Client = { NotConnected = !c.GetProperty("connected").GetBoolean(), FailCalendarList = c.GetProperty("fail_calendar_list").GetBoolean() },
            };
            foreach (var failing in c.GetProperty("fail_events").EnumerateArray())
            {
                rig.Client.FailEventsFor.Add(failing.GetString()!);
            }

            var tokens = Substitute.For<IGoogleTokenRepository>();
            tokens.FindAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
                c.GetProperty("token").GetBoolean() ? new GoogleTokenRecord("a", "r", null, null, false) : null);

            var period = c.GetProperty("period").GetString();
            var duration = c.GetProperty("duration").GetDouble();
            var context = c.GetProperty("contexto").ValueKind == JsonValueKind.String ? c.GetProperty("contexto").GetString() : null;

            var service = new FreeTimeService(rig.Client, tokens, rig.Llm, rig.Clock);
            var result = await service.FindAsync(7, period, duration, CancellationToken.None);

            var expected = c.GetProperty("result");
            result.Error.ShouldBe(expected.TryGetProperty("error", out var error) ? error.GetString() : null, name);
            var slots = expected.GetProperty("slots_livres").EnumerateArray().ToList();
            result.Slots.Count.ShouldBe(slots.Count, name);
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = result.Slots[i];
                slot.Date.ShouldBe(slots[i].GetProperty("data").GetString(), $"{name} #{i}");
                slot.StartTime.ShouldBe(slots[i].GetProperty("hora_inicio").GetString(), $"{name} #{i}");
                slot.EndTime.ShouldBe(slots[i].GetProperty("hora_fim").GetString(), $"{name} #{i}");
                slot.DurationMinutes.ShouldBe(slots[i].GetProperty("duracao_minutos").GetInt32(), $"{name} #{i}");
                slot.Quality.ShouldBe(slots[i].GetProperty("qualidade").GetString(), $"{name} #{i}");
                slot.Reason.ShouldBe(slots[i].GetProperty("motivo").GetString(), $"{name} #{i}");
            }

            if (expected.TryGetProperty("insights_usuario", out var insights))
            {
                result.Insights.ShouldBe(insights.GetString(), name);
                result.TotalAnalyzed.ShouldBe(expected.GetProperty("total_analisado").GetInt32(), name);
            }

            FreeSlotsMessage.Format(result, context).ShouldBe(c.GetProperty("message").GetString(), name);
            rig.Client.Calls.ShouldBe(c.GetProperty("calls").EnumerateArray().Select(CalendarRig.CallText), name);
        }
    }

    [Fact]
    [Trait("AC", "012-506")]
    public async Task FreeSlotsIntent_AppendsTheAiSuggestion_AsTheFlaskDoes()
    {
        var worlds = WhatsAppFixture.Section("free_worlds");
        var cases = CalendarRig.Cases("free_slots_cases").Where(c => c.GetProperty("name").GetString() is "hoje_ocupado" or "proxima_semana_ocupada"
            or "modelo_falha" or "hoje_livre_sem_historico" or "amanha_livre" or "periodo_invalido").ToList();
        cases.Count.ShouldBe(6);

        foreach (var c in cases)
        {
            var name = c.GetProperty("name").GetString()!;
            var rig = new CalendarRig(c.GetProperty("now").GetString()!, worlds.GetProperty(c.GetProperty("world").GetString()!));
            var tokens = Substitute.For<IGoogleTokenRepository>();
            tokens.FindAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns((GoogleTokenRecord?)null);

            var context = c.GetProperty("contexto").ValueKind == JsonValueKind.String ? c.GetProperty("contexto").GetString() : null;
            rig.Llm.Then(JsonSerializer.Serialize(new { period_type = c.GetProperty("period").GetString(), duracao_minutos = c.GetProperty("duration").GetDouble(), contexto = context }));
            if (c.GetProperty("model_fail").GetBoolean())
            {
                rig.Llm.ThenThrow(new LlmException("falha do modelo"));
            }
            else
            {
                rig.Llm.Then(c.GetProperty("model_reply").GetString()!);
            }

            var intent = new FreeSlotsIntent(rig.Llm, new FreeTimeService(rig.Client, tokens, rig.Llm, rig.Clock));
            var reply = await intent.HandleAsync(rig.Context("quando estou livre?"), CancellationToken.None);

            reply.ShouldBe(c.GetProperty("intent_message").GetString(), name);
            rig.Llm.Requests[0].Prompt.ShouldBe(WhatsAppPrompts.FreeTimeQuery("quando estou livre?"), name);
            rig.Llm.Requests[0].UserId.ShouldBe(7, name);
            rig.Llm.Requests[0].Cache.ShouldBeNull(name);

            var prompts = c.GetProperty("prompts").EnumerateArray().Select(p => p.GetString()!).ToList();
            rig.Llm.Requests.Skip(1).Select(r => r.Prompt).ShouldBe(prompts, name);
            rig.Llm.Requests.Skip(1).ShouldAllBe(r => r.UserId == 7, name);
        }
    }

    [Theory]
    [InlineData("""{"period_type": "hoje", "duracao_minutos": null}""")]
    [InlineData("""{"period_type": "hoje", "duracao_minutos": "60"}""")]
    [Trait("AC", "012-506")]
    public async Task FreeSlotsIntent_WithADurationThatIsNotANumber_FailsLikeTheFlask(string json)
    {
        var rig = new CalendarRig("2026-10-08T09:00:00-03:00");
        var tokens = Substitute.For<IGoogleTokenRepository>();
        var intent = new FreeSlotsIntent(rig.Llm.Then(json), new FreeTimeService(rig.Client, tokens, rig.Llm, rig.Clock));

        // O Flask compara int com None/str e levanta TypeError; a intenção "template" responde o texto genérico (IntentRouter).
        await Should.ThrowAsync<InvalidOperationException>(() => intent.HandleAsync(rig.Context(), CancellationToken.None));
    }

    [Fact]
    [Trait("AC", "012-506")]
    public async Task FreeSlotsIntent_PeriodNull_AnswersNoSlots_WithoutLookingAtTheDuration()
    {
        var rig = new CalendarRig("2026-10-08T09:00:00-03:00");
        var tokens = Substitute.For<IGoogleTokenRepository>();
        var intent = new FreeSlotsIntent(rig.Llm.Then("""{"period_type": null, "duracao_minutos": null}"""), new FreeTimeService(rig.Client, tokens, rig.Llm, rig.Clock));

        var reply = await intent.HandleAsync(rig.Context(), CancellationToken.None);

        reply.ShouldBe("🗓️ *Horários Livres*\n\n❌ Nenhum horário livre encontrado no período solicitado.\n\n");
    }

    [Fact]
    [Trait("AC", "012-505")]
    public async Task AgendaIntent_AsksThePeriodAndTheTimeFilter_ThenAnswers()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00", WhatsAppFixture.Section("agenda_world"));
        var service = new AgendaService(rig.Client, CalendarRig.Account(true), CalendarRig.Links("https://h/x", rig.Time.GetUtcNow()), rig.Clock);
        rig.Llm.Then("""{"period_type": "hoje"}""").Then("""{"time_filter": "tarde"}""");

        var reply = await new AgendaIntent(rig.Llm, service).HandleAsync(rig.Context("agenda de hoje à tarde"), CancellationToken.None);

        reply.ShouldBe(CalendarRig.Cases("agenda_cases").Single(c => c.GetProperty("name").GetString() == "hoje_filtro_tarde").GetProperty("text").GetString());
        rig.Llm.Requests.Select(r => r.Prompt).ShouldBe(
            [WhatsAppPrompts.CalendarQuery("agenda de hoje à tarde"), WhatsAppPrompts.TimeFilter("agenda de hoje à tarde")]);
        rig.Llm.Requests.ShouldAllBe(r => r.UserId == 7 && r.Cache == null);
    }

    [Theory]
    [InlineData("""{"time_filter": null}""", "periodo_hoje")]
    [InlineData("""{"time_filter": ""}""", "periodo_hoje")]
    [InlineData("""{}""", "periodo_hoje")]
    public async Task AgendaIntent_WithoutATimeFilter_UsesThePlainQuery(string timeJson, string caseName)
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00", WhatsAppFixture.Section("agenda_world"));
        var service = new AgendaService(rig.Client, CalendarRig.Account(true), CalendarRig.Links("https://h/x", rig.Time.GetUtcNow()), rig.Clock);
        rig.Llm.Then("""{}""").Then(timeJson);

        var reply = await new AgendaIntent(rig.Llm, service).HandleAsync(rig.Context(), CancellationToken.None);

        reply.ShouldBe(CalendarRig.Cases("agenda_cases").Single(c => c.GetProperty("name").GetString() == caseName).GetProperty("text").GetString());
    }

    [Fact]
    [Trait("AC", "012-504")]
    public async Task DeleteEventIntent_MatchesTheFlask_ForEveryScenario()
    {
        var world = WhatsAppFixture.Section("delete_world");
        var cases = CalendarRig.Cases("delete_event_cases");
        cases.Count.ShouldBeGreaterThan(8);

        foreach (var c in cases)
        {
            var name = c.GetProperty("name").GetString()!;
            var disconnected = name == "sem_google";
            var rig = new CalendarRig(c.GetProperty("now").GetString()!, disconnected ? null : world)
            {
                Client = { NotConnected = disconnected },
            };
            rig.Llm.Then(c.GetProperty("query").GetRawText());

            var reply = await new DeleteEventIntent(rig.Llm, rig.Client, rig.Clock).HandleAsync(rig.Context("deletar"), CancellationToken.None);

            reply.ShouldBe(c.GetProperty("message").GetString(), name);
            rig.Llm.Requests.Single().Prompt.ShouldBe(WhatsAppPrompts.EventDeletion("deletar"), name);
            rig.Llm.Requests.Single().UserId.ShouldBe(7, name);

            var deletes = c.GetProperty("calls").EnumerateArray().Where(call => call[0].GetString() == "delete")
                .Select(call => (call[2].GetString()!, call[1].GetString()!)).ToList();
            rig.Client.Deleted.ShouldBe(deletes, name);
        }
    }

    [Fact]
    [Trait("AC", "012-504")]
    public async Task DeleteEventIntent_KeepsOnlyTheFirstFivePerCalendar()
    {
        var events = Enumerable.Range(1, 8).Select(i => new
        {
            id = $"p{i}",
            summary = $"Aula {i}",
            start = new { dateTime = $"2026-10-{10 + i}T10:00:00-03:00" },
            end = new { dateTime = $"2026-10-{10 + i}T11:00:00-03:00" },
        }).ToList();
        var world = JsonSerializer.SerializeToElement(new[] { new { id = "primary", summary = "Principal", selected = true, primary = true, events } });
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00", world);
        rig.Llm.Then("""{"titulo_busca": "aula", "quando": null}""");

        var reply = await new DeleteEventIntent(rig.Llm, rig.Client, rig.Clock).HandleAsync(rig.Context(), CancellationToken.None);

        reply.ShouldStartWith("📋 Encontrei 5 eventos:");
        reply.ShouldNotContain("Aula 6");
    }

    [Fact]
    [Trait("AC", "012-504")]
    public async Task DeleteEventIntent_WhenTheDeletionFails_AnswersWithoutTheReason()
    {
        var client = Substitute.For<ICalendarClient>();
        client.FindEventsByTitleAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            [new CalendarEvent("a1", "Dentista", "2026-10-09T10:00:00-03:00", "2026-10-09T11:00:00-03:00", "", "", false, "Principal", "primary")]);
        client.DeleteEventAsync(Arg.Any<int>(), "a1", "primary", Arg.Any<CancellationToken>())
            .Returns<CalendarDeleteResult>(_ => throw new CalendarApiException("Falha na API do Google Calendar (HTTP 500)", null));
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        rig.Llm.Then("""{"titulo_busca": "dentista", "quando": null}""");

        var reply = await new DeleteEventIntent(rig.Llm, client, rig.Clock).HandleAsync(rig.Context(), CancellationToken.None);

        reply.ShouldBe("❌ Erro ao deletar evento.");
    }

    [Fact]
    [Trait("AC", "012-504")]
    public async Task DeleteEventIntent_WhenTheEventIsAlreadyGone_SaysSo()
    {
        var client = Substitute.For<ICalendarClient>();
        client.FindEventsByTitleAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            [new CalendarEvent("a1", "Dentista", "2026-10-09T10:00:00-03:00", "2026-10-09T11:00:00-03:00", "", "", false, "Principal", "primary")]);
        client.DeleteEventAsync(Arg.Any<int>(), "a1", "primary", Arg.Any<CancellationToken>()).Returns(new CalendarDeleteResult(false, "Dentista"));
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        rig.Llm.Then("""{"titulo_busca": "dentista", "quando": null}""");

        var reply = await new DeleteEventIntent(rig.Llm, client, rig.Clock).HandleAsync(rig.Context(), CancellationToken.None);

        reply.ShouldBe("❌ Evento não encontrado ou já foi deletado.");
    }

    [Fact]
    [Trait("AC", "012-501")]
    public async Task CreateEventIntent_MatchesTheFlask_ForEveryScenario()
    {
        var cases = CalendarRig.Cases("create_event_cases");
        cases.Count.ShouldBeGreaterThan(10);

        foreach (var c in cases)
        {
            var name = c.GetProperty("name").GetString()!;
            var rig = new CalendarRig(c.GetProperty("now").GetString()!, addressCount: c.GetProperty("addresses").GetInt32());
            rig.Llm.Then(c.GetProperty("data").GetRawText());

            var intent = new CreateEventIntent(rig.Llm, rig.Events, rig.UserAddresses, rig.Clock);
            var reply = await intent.HandleAsync(rig.Context("criar"), CancellationToken.None);

            reply.ShouldBe(c.GetProperty("message").GetString(), name);
            rig.Llm.Requests.Single().Prompt.ShouldBe(WhatsAppPrompts.EventCreation("criar", rig.Clock.Today), name);
            rig.Llm.Requests.Single().UserId.ShouldBe(7, name);

            var expectedKeys = c.GetProperty("redis").EnumerateObject().Select(p => p.Name).ToList();
            var keys = (await rig.Cache.KeysByPatternAsync("*")).ToList();
            keys.ShouldBe(expectedKeys, name);
            foreach (var entry in c.GetProperty("redis").EnumerateObject())
            {
                JsonNode.DeepEquals(JsonNode.Parse(rig.Cache.RawValue(entry.Name)!), JsonNode.Parse(entry.Value.GetRawText())).ShouldBeTrue($"{name}: {entry.Name}");
                rig.Cache.TtlOf(entry.Name).ShouldBe(TimeSpan.FromSeconds(300), name);
            }
        }
    }

    [Fact]
    [Trait("AC", "012-501")]
    public async Task CreateEventIntent_WhenRedisFails_AnswersTheProcessingError()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        rig.Cache.Unavailable = true;
        rig.Llm.Then("""{"titulo": "Algo", "data": "hoje"}""");

        var reply = await new CreateEventIntent(rig.Llm, rig.Events, rig.UserAddresses, rig.Clock).HandleAsync(rig.Context(), CancellationToken.None);

        reply.ShouldBe("❌ Erro ao processar evento. Tente novamente.");
    }

    [Fact]
    [Trait("AC", "012-501")]
    public async Task CreateEventIntent_WhenAddressLookupFails_SuggestsRegisteringAnAddress()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        rig.Addresses.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<AddressRow>>(_ => throw new InvalidOperationException("banco fora"));
        rig.Llm.Then("""{"titulo": "Reunião", "data": "hoje", "localizacao": "Sala 3"}""");

        var reply = await new CreateEventIntent(rig.Llm, rig.Events, rig.UserAddresses, rig.Clock).HandleAsync(rig.Context(), CancellationToken.None);

        reply.ShouldContain("💡 *Dica:* Configure seus endereços favoritos");
    }

    [Theory]
    [InlineData("""[1, 2]""")]
    [InlineData("""null""")]
    [InlineData("""texto""")]
    public async Task CreateEventIntent_WhenGeminiDoesNotReturnAnObject_Throws(string reply)
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        rig.Llm.Then(reply);

        var act = () => new CreateEventIntent(rig.Llm, rig.Events, rig.UserAddresses, rig.Clock).HandleAsync(rig.Context(), CancellationToken.None);

        await Should.ThrowAsync<Exception>(act);
    }
}
