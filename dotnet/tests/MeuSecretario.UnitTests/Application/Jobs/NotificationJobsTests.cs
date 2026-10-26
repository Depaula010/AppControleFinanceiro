using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MeuSecretario.UnitTests.Application.Jobs;

public class DailyBriefingJobTests
{
    private readonly JobsHarness _h = new();
    private readonly IJobNotificationConfigRepository _config = Substitute.For<IJobNotificationConfigRepository>();
    private readonly IBriefingDataRepository _users = Substitute.For<IBriefingDataRepository>();
    private readonly ICalendarClient _calendar = Substitute.For<ICalendarClient>();
    private readonly IWeatherClient _weather = Substitute.For<IWeatherClient>();
    private readonly IFinancialAlertsRepository _alerts = Substitute.For<IFinancialAlertsRepository>();
    private readonly ILlmClient _llm = Substitute.For<ILlmClient>();

    public DailyBriefingJobTests()
    {
        _h.At(7);
        _users.GetLocationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new BriefingUser("São Paulo", "SP", "5511"));
        _alerts.GetUpcomingAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(FinancialAlerts.Empty);
        _calendar.GetEventsInRangeAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _config.ListMorningUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns([new NotifiedUser(1, "5511")]);
    }

    private DailyBriefingService Service() => new(_users, _calendar, _weather, _alerts, _llm, _h.Log);

    private DailyBriefingJob Job() => new(_config, Service(), _alerts, _h.Bot, _users, _h.Clock, _h.Log);

    private static FinancialAlerts OneBill() => new(
        [new AlertBill(1, "Aluguel", 1500, "Moradia", "Conta", AlertBill.Expense)], [], [], []);

    [Theory]
    [Trait("AC", "013-21")]
    [InlineData(true, true, "R", "A", "R")]
    [InlineData(true, true, null, "A", "A")]
    [InlineData(true, true, null, null, null)]
    [InlineData(true, false, "R", "A", "R")]
    [InlineData(true, false, null, "A", null)]
    [InlineData(false, true, "R", "A", "A")]
    [InlineData(false, true, "R", null, null)]
    [InlineData(false, false, "R", "A", null)]
    public void Unify_FollowsTheFlagMatrix(bool summaryActive, bool alertsActive, string? summary, string? alerts, string? expected) =>
        DailyBriefingJob.Unify(summaryActive, alertsActive, summary, alerts).ShouldBe(expected);

    [Fact]
    [Trait("AC", "013-20")]
    public async Task Execute_SelectsByTheCycleHour_WithSecondsDropped()
    {
        _h.At(7, 0, 42);
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, true));

        await Job().ExecuteAsync(CancellationToken.None);

        await _config.Received(1).ListMorningUsersAsync(new TimeOnly(7, 0), Arg.Any<CancellationToken>());
    }

    [Theory]
    [Trait("AC", "013-20")]
    [InlineData(7, 30)]
    [InlineData(6, 1)]
    [InlineData(23, 59)]
    public async Task Execute_SelectsByTheMinuteOfTheCycle_Deviation136(int hour, int minute)
    {
        _h.At(hour, minute, 15);
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, true));

        await Job().ExecuteAsync(CancellationToken.None);

        await _config.Received(1).ListMorningUsersAsync(new TimeOnly(hour, minute), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-20")]
    public async Task Execute_NoUsers_SendsNothing()
    {
        _config.ListMorningUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns([]);

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "013-24")]
    public async Task Execute_NoEvents_SendsTheSimpleMessageWithWeatherAndAlerts()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, true));
        _weather.GetWeatherAsync("São Paulo", "SP", 1, Arg.Any<CancellationToken>()).Returns(new WeatherInfo(24, "Nublado", "☁", 24, 60, 70, "Nublado, 24°C"));
        _alerts.GetUpcomingAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(OneBill());

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m =>
            m.StartsWith("☀️ Bom dia! Você não tem compromissos agendados para hoje! 🎉\n\n", StringComparison.Ordinal)
            && m.Contains("Clima: Nublado, 24°C", StringComparison.Ordinal)
            && m.Contains("• Aluguel - R$ 1500,00", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-23")]
    public async Task Execute_WithEvents_AsksTheModelWithTheUsersKey_AndSendsTheCleanedText()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, false));
        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new CalendarEvent("e", "Daily", "2026-03-18T09:00:00-03:00", "2026-03-18T09:30:00-03:00", "", "", false, null)]);
        _llm.GenerateTextAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>()).Returns("  <b>Bom dia!</b>  ");

        await Job().ExecuteAsync(CancellationToken.None);

        await _llm.Received(1).GenerateTextAsync(
            Arg.Is<LlmRequest>(r => r.UserId == 1 && r.Cache == null && r.Prompt.Contains("1. 09:00 (30 min) - Daily", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await _h.Bot.Received(1).SendTextAsync("5511", "Bom dia!", Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-23")]
    public async Task Execute_ModelFails_SendsTheBasicMessage()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, false));
        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new CalendarEvent("e", "Daily", "2026-03-18T09:00:00-03:00", "2026-03-18T09:30:00-03:00", "", "", false, null)]);
        _llm.GenerateTextAsync(Arg.Any<LlmRequest>(), Arg.Any<CancellationToken>()).Throws(new LlmException("x"));

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.StartsWith("☀️ Bom dia! Sua agenda de hoje:", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-24")]
    public async Task Execute_BothActiveAndGoogleDisconnected_StillSendsTheFinancialAlerts_Deviation137()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, true));
        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Throws(new GoogleNotConnectedException("x"));
        _alerts.GetUpcomingAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(OneBill());

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.StartsWith("🌅 *Bom dia!*\n\n💰 *ALERTAS FINANCEIROS*", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-24")]
    public async Task Execute_BothActiveGoogleDisconnectedAndNothingDue_SendsNothing()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, true));
        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Throws(new GoogleNotConnectedException("x"));

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "013-24")]
    public async Task Execute_SummaryActiveOnly_AndGoogleDisconnected_SendsNothing()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, false));
        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Throws(new GoogleNotConnectedException("x"));
        _alerts.GetUpcomingAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(OneBill());

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "013-24")]
    public async Task Execute_SummaryThrowsUnexpectedly_StillSendsTheFinancialAlerts_Deviation137()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(true, true));
        _users.GetLocationAsync(1, Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("x"));
        _alerts.GetUpcomingAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(OneBill());

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.Contains("ALERTAS FINANCEIROS", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-24")]
    public async Task Execute_OnlyAlertsActive_SendsAlertsWithGreeting()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(false, true));
        _alerts.GetUpcomingAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(OneBill());

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.StartsWith("🌅 *Bom dia!*\n\n💰 *ALERTAS FINANCEIROS*", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await _calendar.DidNotReceiveWithAnyArgs().GetEventsInRangeAsync(default, default, default, default);
        _h.Log.Lines.ShouldNotContain(l => l.StartsWith("ERROR", StringComparison.Ordinal));
        _h.Log.Lines.ShouldContain(l => l.Contains("Mensagem enviada", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-24")]
    public async Task Execute_BotFailure_IsLoggedAsAnError_NotAsSent_Deviation139()
    {
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(false, true));
        _alerts.GetUpcomingAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(OneBill());
        _h.Bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await Job().ExecuteAsync(CancellationToken.None);

        _h.Log.Lines.ShouldContain(l => l.StartsWith("ERROR", StringComparison.Ordinal) && l.Contains("Falha ao enviar", StringComparison.Ordinal));
        _h.Log.Lines.ShouldNotContain(l => l.Contains("Mensagem enviada", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-24")]
    public async Task Execute_AnUserFailing_DoesNotStopTheNext()
    {
        _config.ListMorningUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns([new NotifiedUser(1, "5511"), new NotifiedUser(2, "5522")]);
        _config.GetOrCreateAsync(1, Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("x"));
        _config.GetOrCreateAsync(2, Arg.Any<CancellationToken>()).Returns(new UserNotificationConfig(false, true));
        _alerts.GetUpcomingAsync(2, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(OneBill());

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5522", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-22")]
    public async Task Prepare_CollectsWeatherExtraCitiesGapsTypesAndAlerts()
    {
        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
        [
            new CalendarEvent("a", "Call", "2026-03-18T09:00:00-03:00", "2026-03-18T10:00:00-03:00", "", "", false, null),
            new CalendarEvent("b", "Almoço", "2026-03-18T12:00:00-03:00", "2026-03-18T13:00:00-03:00", "Rest, Campinas, SP", "", false, null),
            new CalendarEvent("c", "Reunião", "2026-03-18T14:00:00-03:00", "2026-03-18T15:00:00-03:00", "Av X, São Paulo, SP", "", false, null),
        ]);
        _weather.GetWeatherAsync("São Paulo", "SP", 1, Arg.Any<CancellationToken>()).Returns(new WeatherInfo(20, "c", "e", 20, 1, 10, "SP 20"));
        _weather.GetWeatherAsync("Campinas", "SP", 1, Arg.Any<CancellationToken>()).Returns(new WeatherInfo(25, "c", "e", 25, 1, 10, "CPS 25"));
        _alerts.GetUpcomingAsync(1, new DateOnly(2026, 3, 18), Arg.Any<CancellationToken>()).Returns(OneBill());

        var data = await Service().PrepareAsync(1, new DateOnly(2026, 3, 18), CancellationToken.None);

        data.ShouldNotBeNull();
        data.TotalEvents.ShouldBe(3);
        data.MainWeather!.FullDescription.ShouldBe("SP 20");
        data.ExtraWeather.Select(w => w.City).ShouldBe(["Campinas"]);
        data.Gaps.Select(g => (g.Start, g.End, g.Minutes)).ShouldBe([("10:00", "12:00", 120), ("13:00", "14:00", 60)]);
        data.RemoteEvents.ShouldBe(1);
        data.InPersonEvents.ShouldBe(2);
        data.Alerts.Any.ShouldBeTrue();
        await _weather.Received(1).GetWeatherAsync("São Paulo", "SP", 1, Arg.Any<CancellationToken>());
        await _weather.DidNotReceive().GetWeatherAsync(Arg.Any<string>(), Arg.Any<string?>(), null, Arg.Any<CancellationToken>()); // desvio 138
    }

    [Fact]
    [Trait("AC", "013-22")]
    public async Task Prepare_AlertsFailure_BecomesEmptyAlerts_AndGoogleFailureBecomesNull()
    {
        _alerts.GetUpcomingAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("x"));

        var data = await Service().PrepareAsync(1, new DateOnly(2026, 3, 18), CancellationToken.None);

        data!.Alerts.Any.ShouldBeFalse();
        _calendar.GetEventsInRangeAsync(2, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Throws(new GoogleNotConnectedException("x"));
        (await Service().PrepareAsync(2, new DateOnly(2026, 3, 18), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "013-25")]
    public async Task Trigger_IgnoresTheFlags_AndCountsSentAndErrors()
    {
        _config.ListMorningUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>())
            .Returns([new NotifiedUser(1, "5511"), new NotifiedUser(2, "5522"), new NotifiedUser(3, "5533")]);
        _calendar.GetEventsInRangeAsync(2, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Throws(new GoogleNotConnectedException("x"));
        _h.Bot.SendTextAsync("5533", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Job().RunForCurrentMinuteAsync(CancellationToken.None);

        result.ShouldBe(new DailyBriefingRunResult(3, 1, 2, "07:00", NoUsers: false));
        await _config.DidNotReceiveWithAnyArgs().GetOrCreateAsync(default, default);
    }

    [Fact]
    [Trait("AC", "013-25")]
    public async Task Trigger_NoUsers_ReportsTheMinute()
    {
        _config.ListMorningUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns([]);

        (await Job().RunForCurrentMinuteAsync(CancellationToken.None)).ShouldBe(new DailyBriefingRunResult(0, 0, 0, "07:00", NoUsers: true));
    }

    [Fact]
    [Trait("AC", "013-25")]
    public async Task SendTest_CoversTheFourOutcomes()
    {
        (await Job().SendTestAsync(1, CancellationToken.None)).Outcome.ShouldBe(DailyBriefingTestOutcome.Sent);

        _h.Bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        (await Job().SendTestAsync(1, CancellationToken.None)).Outcome.ShouldBe(DailyBriefingTestOutcome.SendFailed);

        _calendar.GetEventsInRangeAsync(1, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Throws(new GoogleNotConnectedException("x"));
        (await Job().SendTestAsync(1, CancellationToken.None)).Outcome.ShouldBe(DailyBriefingTestOutcome.PrepareFailed);
    }
}

public class BillsNotificationJobTests
{
    private readonly JobsHarness _h = new();
    private readonly IJobNotificationConfigRepository _config = Substitute.For<IJobNotificationConfigRepository>();
    private readonly IBillsReminderRepository _bills = Substitute.For<IBillsReminderRepository>();

    [Fact]
    [Trait("AC", "013-32")]
    public async Task Run_UsesTheFiveMinuteWindow_TomorrowAndCountsResults()
    {
        _h.At(7, 0, 30);
        _config.ListBillsReminderUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns(
            [new BillsReminderUser(1, "5511", "Ana"), new BillsReminderUser(2, "5522", "Bia"), new BillsReminderUser(3, "5533", "Cris")]);
        var list = new List<ReminderBill> { new("Aluguel", 1500, "Moradia", "Conta Corrente", "Despesa Essencial") };
        _bills.ListAsync(1, new DateOnly(2026, 3, 19), new DateOnly(2026, 3, 18), Arg.Any<CancellationToken>()).Returns(list);
        _bills.ListAsync(2, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new List<ReminderBill>());
        _bills.ListAsync(3, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("x"));

        var result = await new BillsNotificationJob(_config, _bills, _h.Bot, _h.Clock, _h.Log).RunAsync(CancellationToken.None);

        await _config.Received(1).ListBillsReminderUsersAsync(new TimeOnly(6, 55), new TimeOnly(7, 5), Arg.Any<CancellationToken>());
        result.UsersProcessed.ShouldBe(3);
        result.NotificationsSent.ShouldBe(1);
        result.Errors.ShouldBe(1);
        result.ProcessedAt.ShouldBe("2026-03-18T07:00:30-03:00");
        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.StartsWith("🔔 *Lembrete de Contas, Ana!*", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }
}

public class NightlyCheckinJobTests
{
    private readonly JobsHarness _h = new();
    private readonly IJobNotificationConfigRepository _config = Substitute.For<IJobNotificationConfigRepository>();
    private readonly ICheckinQueries _queries = Substitute.For<ICheckinQueries>();

    public NightlyCheckinJobTests()
    {
        _h.At(20);
        _config.ListCheckinUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns([new NotifiedUser(1, "5511")]);
    }

    private NightlyCheckinJob Job() => new(_config, new NightlyCheckinService(_queries, _h.Cache, _h.Clock), _h.Bot, _h.Cache, _h.Clock, _h.Log);

    private static CheckinBill Bill(int id, string group = "Despesa Essencial", int day = 18) => new(
        id, "Aluguel", 1500m, day, 9, 9, 1, "Conta", "Conta Corrente", "Moradia", "Macro", group, "FIXO", 0, null, new DateOnly(2026, 3, 18));

    [Fact]
    [Trait("AC", "013-40")]
    public async Task Execute_RedisDown_FailsWithoutSending()
    {
        _h.Cache.Unavailable = true;

        await Should.ThrowAsync<InvalidOperationException>(() => Job().ExecuteAsync(CancellationToken.None));

        await _h.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    [Trait("AC", "013-40")]
    public async Task Execute_OutsideTheWindow_WarnsButGoesOn()
    {
        _h.At(9);
        _queries.GetSnapshotAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new CheckinSnapshot([], [], [], []));

        await Job().ExecuteAsync(CancellationToken.None);

        _h.Log.Lines.ShouldContain(l => l.StartsWith("WARNING", StringComparison.Ordinal) && l.Contains("fora da janela", StringComparison.Ordinal));
        await _config.Received(1).ListCheckinUsersAsync(new TimeOnly(9, 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-47")]
    public async Task Execute_UserWithoutPending_GetsNoSessionAndNoMessage()
    {
        _queries.GetSnapshotAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new CheckinSnapshot([], [], [], []));

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
        _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5511")).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "013-44")]
    public async Task Execute_CreatesTheSessionInThePythonFormat_AndSendsOneMessage()
    {
        _queries.GetSnapshotAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CheckinSnapshot([Bill(1, "Renda"), Bill(2)], [], [], []));

        await Job().ExecuteAsync(CancellationToken.None);

        var sent = _h.Bot.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IBotClient.SendTextAsync)).GetArguments();
        var message = (string)sent[1]!;
        message.ShouldStartWith("🌙 *CHECK-IN NOTURNO* 🌙");
        var id = _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5511"))!.Trim('"');
        id.Length.ShouldBe(8);
        message.ShouldEndWith($"_ID: {id} | Expira em 1h_");
        _h.Cache.TtlOf(CacheKeys.NightlyCheckinActive("5511"))!.Value.TotalSeconds.ShouldBeInRange(3599, 3600);
        using var session = JsonDocument.Parse(_h.Cache.RawValue(CacheKeys.NightlyCheckin("5511", id))!);
        session.RootElement.GetProperty("total_items").GetInt32().ShouldBe(2);
        session.RootElement.GetProperty("created_at").GetString().ShouldBe("2026-03-18");
        session.RootElement.GetProperty("items").GetProperty("1").GetProperty("nome_grupo").GetString().ShouldBe("Renda");
        session.RootElement.GetProperty("items").GetProperty("2").GetProperty("data_vencimento_real").GetString().ShouldBe("2026-03-18");
    }

    [Fact]
    [Trait("AC", "013-46")]
    public async Task Execute_OverdueInvoice_SendsTheMessageWithTheCardName_AndCreatesTheSession_Deviation130()
    {
        _queries.GetSnapshotAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CheckinSnapshot([Bill(1)], [], [new OverdueInvoice(1, "Inter", new DateOnly(2026, 3, 10), "Aberta", 10m, AccountId: 7)], []));

        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.Contains("🔴 *FATURAS VENCIDAS:*\n2. Inter - R$ 10,00 - Venceu em 10/03/2026", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5511")).ShouldNotBeNull();
    }

    [Fact]
    [Trait("AC", "013-46")]
    public async Task Execute_OverdueInvoice_IsNumberedAfterTheBills_AndStoredInTheSession_Deviation264()
    {
        _queries.GetSnapshotAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new CheckinSnapshot([Bill(1)], [], [new OverdueInvoice(9, "Inter", new DateOnly(2026, 3, 10), "Fechada", 405.73m, AccountId: 7)], []));

        await Job().ExecuteAsync(CancellationToken.None);

        var id = _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5511"))!.Trim('"');
        var session = System.Text.Json.JsonSerializer.Deserialize<CheckinSession>(_h.Cache.RawValue(CacheKeys.NightlyCheckin("5511", id))!)!;
        session.TotalItems.ShouldBe(2);
        session.Items.Keys.ShouldBe(["1"]);
        var invoice = session.Invoices["2"];
        (invoice.InvoiceId, invoice.CardAccountId, invoice.UserId, invoice.Card, invoice.Amount).ShouldBe((9, 7, 1, "Inter", 405.73));
    }

    [Fact]
    [Trait("AC", "013-46")]
    public async Task Execute_MessageFailsToBuild_NoSessionIsCreated_AndTheNextUserStillGetsTheirs_Deviation130()
    {
        _config.ListCheckinUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns([new NotifiedUser(1, "5511"), new NotifiedUser(2, "5522")]);
        _queries.GetSnapshotAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("x"));
        _queries.GetSnapshotAsync(2, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new CheckinSnapshot([Bill(2)], [], [], []));

        await Job().ExecuteAsync(CancellationToken.None);

        _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5511")).ShouldBeNull();
        await _h.Bot.DidNotReceive().SendTextAsync("5511", Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _h.Bot.Received(1).SendTextAsync("5522", Arg.Any<string>(), Arg.Any<CancellationToken>());
        _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5522")).ShouldNotBeNull();
    }

    [Fact]
    [Trait("AC", "013-46")]
    public async Task Execute_SendFails_RemovesTheSessionAndTheActiveFlag_AndLogsTheFailure_Deviation139()
    {
        _config.ListCheckinUsersAsync(Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns([new NotifiedUser(1, "5511"), new NotifiedUser(2, "5522")]);
        _queries.GetSnapshotAsync(Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new CheckinSnapshot([Bill(1)], [], [], []));
        _h.Bot.SendTextAsync("5511", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await Job().ExecuteAsync(CancellationToken.None);

        _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5511")).ShouldBeNull();
        (await _h.Cache.KeysByPatternAsync("nightly_checkin:5511:*")).ShouldBeEmpty();
        _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5522")).ShouldNotBeNull(); // o próximo usuário recebeu
        _h.Log.Lines.ShouldContain(l => l.StartsWith("ERROR", StringComparison.Ordinal) && l.Contains("Falha ao enviar o check-in", StringComparison.Ordinal));
        _h.Log.Lines.ShouldNotContain(l => l.Contains("Check-in consolidado enviado para usuário 1", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-46")]
    public async Task Execute_SendThrows_RemovesTheSession_Deviation139()
    {
        _queries.GetSnapshotAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new CheckinSnapshot([Bill(1)], [], [], []));
        _h.Bot.SendTextAsync("5511", Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new HttpRequestException("x"));

        await Job().ExecuteAsync(CancellationToken.None);

        _h.Cache.RawValue(CacheKeys.NightlyCheckinActive("5511")).ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "013-41")]
    [InlineData(19, 15)]
    [InlineData(18, 0)]
    [InlineData(23, 59)]
    public async Task Execute_SelectsByTheMinuteOfTheCycle_Deviation136(int hour, int minute)
    {
        _h.At(hour, minute, 42);
        _queries.GetSnapshotAsync(1, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new CheckinSnapshot([], [], [], []));

        await Job().ExecuteAsync(CancellationToken.None);

        await _config.Received(1).ListCheckinUsersAsync(new TimeOnly(hour, minute), Arg.Any<CancellationToken>());
    }
}

public class MonthlyReportJobTests
{
    private readonly JobsHarness _h = new();
    private readonly IMonthlyReportRepository _repository = Substitute.For<IMonthlyReportRepository>();
    private readonly IChartRenderer _chart = Substitute.For<IChartRenderer>();

    public MonthlyReportJobTests()
    {
        _h.At(8);
        _chart.RenderPie(Arg.Any<IReadOnlyList<(string, double)>>(), Arg.Any<int>()).Returns([1, 2, 3]);
    }

    private MonthlyReportJob Job() => new(_repository, _h.Bot, _chart, _h.Clock, _h.Log);

    private static MonthlyReportData Data(int transactions) => new(
        2, 2026, new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), new ReportTotals(10, 100, 90, transactions),
        [new ReportCategory("Mercado", 10, 100)], new ReportComparison(10, 0, 10, 0), [], new ReportAccounts(1, 10, 0, 0));

    private void Recipients(params ReportRecipient[] recipients) =>
        _repository.ListRecipientsAsync(Arg.Any<MonthlyMoment>(), Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>()).Returns(recipients);

    [Fact]
    [Trait("AC", "013-70")]
    public async Task Run_UsesTheWindowAroundNow_AndTheMomentName()
    {
        _h.At(8, 0, 30);
        Recipients();

        var result = await Job().RunAsync(MonthlyMoment.InicioMes, CancellationToken.None);

        await _repository.Received(1).ListRecipientsAsync(MonthlyMoment.InicioMes, new TimeOnly(7, 55, 30), new TimeOnly(8, 5, 30), Arg.Any<CancellationToken>());
        result.Moment.ShouldBe("INICIO_MES");
        result.ProcessedAt.ShouldBe("2026-03-18 08:00:30");
    }

    [Fact]
    [Trait("AC", "013-73")]
    public async Task Run_CoversNoDataTextFailureChartFailureAndSuccess()
    {
        Recipients(
            new ReportRecipient(1, "Sem Dados", "5511", new TimeOnly(8, 0)),
            new ReportRecipient(2, "Texto Falha", "5522", new TimeOnly(8, 0)),
            new ReportRecipient(3, "Grafico Falha", "5533", new TimeOnly(8, 0)),
            new ReportRecipient(4, "Ok", "5544", new TimeOnly(8, 0)));
        _repository.GetDataAsync(1, Arg.Any<MonthlyMoment>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Data(0));
        foreach (var id in new[] { 2, 3, 4 })
        {
            _repository.GetDataAsync(id, Arg.Any<MonthlyMoment>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Data(5));
        }

        _h.Bot.SendTextAsync("5522", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _h.Bot.SendImageAsync("5533", Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Job().RunAsync(MonthlyMoment.InicioMes, CancellationToken.None);

        result.TotalUsers.ShouldBe(4);
        result.Sent.ShouldBe(2);
        result.Failed.ShouldBe(1);
        result.Users.Select(u => u.Status).ShouldBe(["sem_dados", "erro", "enviado", "enviado"]);
        result.Users[1].Message.ShouldBe("Falha ao enviar mensagem de texto");
        result.Users[2].ChartSent.ShouldBe(false);
        result.Users[3].ChartSent.ShouldBe(true);
        result.Users[3].ConfiguredHour.ShouldBe("08:00:00");
        result.Errors.Single().Error.ShouldBe("Falha ao enviar mensagem de texto");
        await _h.Bot.Received(1).SendImageAsync("5544", Arg.Any<ReadOnlyMemory<byte>>(), "📊 Relatório de Fevereiro/2026", Arg.Any<CancellationToken>());
        await _h.Bot.DidNotReceive().SendTextAsync("5511", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-73")]
    public async Task Run_GeneralFailure_GoesToErrorsAsGeneral()
    {
        _repository.ListRecipientsAsync(Arg.Any<MonthlyMoment>(), Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("detalhe-interno"));

        var result = await Job().RunAsync(MonthlyMoment.FimMes, CancellationToken.None);

        result.Errors.Single().Type.ShouldBe("erro_geral");
        result.Errors.Single().Error.ShouldNotContain("detalhe-interno", Case.Insensitive);
    }

    [Fact]
    [Trait("AC", "013-73")]
    public async Task Run_NoChart_StillCountsAsSent()
    {
        _chart.RenderPie(Arg.Any<IReadOnlyList<(string, double)>>(), Arg.Any<int>()).Returns((byte[]?)null);
        Recipients(new ReportRecipient(4, "Ok", "5544", new TimeOnly(8, 0)));
        _repository.GetDataAsync(4, Arg.Any<MonthlyMoment>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Data(5));

        var result = await Job().RunAsync(MonthlyMoment.InicioMes, CancellationToken.None);

        result.Sent.ShouldBe(1);
        result.Users.Single().ChartSent.ShouldBe(false);
        await _h.Bot.DidNotReceiveWithAnyArgs().SendImageAsync(default!, default, default, default);
    }

    [Fact]
    [Trait("AC", "013-74")]
    public async Task SendManual_CoversTheFourOutcomes()
    {
        (await Job().SendManualAsync(9, MonthlyMoment.InicioMes, CancellationToken.None)).Error.ShouldBe("Usuário não encontrado");

        var user = new ReportRecipient(1, "Ana", "5511", default);
        _repository.FindUserAsync(1, Arg.Any<CancellationToken>()).Returns(user);
        _repository.GetDataAsync(1, Arg.Any<MonthlyMoment>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Data(0));
        var empty = await Job().SendManualAsync(1, MonthlyMoment.InicioMes, CancellationToken.None);
        empty.Error.ShouldBe("Nenhuma transação encontrada no período");
        empty.Data.ShouldNotBeNull();

        _repository.GetDataAsync(1, Arg.Any<MonthlyMoment>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(Data(5));
        _h.Bot.SendTextAsync("5511", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        (await Job().SendManualAsync(1, MonthlyMoment.InicioMes, CancellationToken.None)).Error.ShouldBe("Falha ao enviar mensagem de texto");

        _h.Bot.SendTextAsync("5511", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var ok = await Job().SendManualAsync(1, MonthlyMoment.InicioMes, CancellationToken.None);
        ok.Success.ShouldBeTrue();
        (ok.MessageSent, ok.ChartSent, ok.Name).ShouldBe((true, true, "Ana"));
    }

    [Theory]
    [Trait("AC", "013-70")]
    [InlineData(2026, 3, 30, false)]
    [InlineData(2026, 3, 31, true)]
    [InlineData(2026, 2, 28, true)]
    [InlineData(2028, 2, 28, false)]
    [InlineData(2028, 2, 29, true)]
    public async Task EndOfMonthJob_RunsOnlyOnTheLastDay(int year, int month, int day, bool runs)
    {
        _h.SetToday(year, month, day, 23);
        Recipients();
        var job = new EndOfMonthReportJob(Job(), _h.Clock, _h.Log);

        await job.ExecuteAsync(CancellationToken.None);

        await _repository.Received(runs ? 1 : 0).ListRecipientsAsync(MonthlyMoment.FimMes, Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-70")]
    public async Task StartOfMonthJob_RunsTheReportForTheStartMoment()
    {
        Recipients();

        await new StartOfMonthReportJob(Job(), _h.Log).ExecuteAsync(CancellationToken.None);

        await _repository.Received(1).ListRecipientsAsync(MonthlyMoment.InicioMes, Arg.Any<TimeOnly>(), Arg.Any<TimeOnly>(), Arg.Any<CancellationToken>());
    }
}
