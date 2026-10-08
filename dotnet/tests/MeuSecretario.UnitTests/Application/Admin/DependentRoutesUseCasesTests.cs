using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using MeuSecretario.Domain.Jobs;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Admin;

public class SendTestNotificationTests
{
    private static readonly AdminField Agenda = AdminField.OfText("agenda");
    private static readonly AdminField Bills = AdminField.OfText("contas");

    private readonly IAdminTestDataRepository _repository = Substitute.For<IAdminTestDataRepository>();
    private readonly IGoogleAccountService _accounts = Substitute.For<IGoogleAccountService>();
    private readonly ICalendarClient _calendar = Substitute.For<ICalendarClient>();
    private readonly IBotClient _bot = Substitute.For<IBotClient>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public SendTestNotificationTests()
    {
        // 2026-10-30 (sexta): amanhã é dia 31.
        _clock.Today.Returns(new DateOnly(2026, 10, 30));
        _repository.FindUserAsync(7, Arg.Any<CancellationToken>()).Returns(new TestUser("Ana", "5511999990001"));
        _accounts.IsConnectedAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        _bot.SendTextAsync(default!, default!, default).ReturnsForAnyArgs(true);
    }

    private SendTestNotification UseCase() => new(_repository, _accounts, _calendar, _bot, _clock);

    private static CalendarEvent Event(string title) =>
        new("id", title, "2026-10-30T10:00:00-03:00", "2026-10-30T11:00:00-03:00", string.Empty, string.Empty, false, null);

    private string SentMessage() => (string)_bot.ReceivedCalls().Single().GetArguments()[1]!;

    [Fact]
    public async Task UnknownUser_IsNotFound_BeforeValidatingTheType()
    {
        _repository.FindUserAsync(99, Arg.Any<CancellationToken>()).Returns((TestUser?)null);

        var result = await UseCase().ExecuteAsync(AdminField.OfText("tipo-invalido"), AdminField.OfWhole(99), default);

        result.ShouldBeOfType<SendTestNotificationResult.UserNotFound>();
        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-3L)]
    [InlineData(2147483648L)]
    public async Task IdThatCannotBeAUser_IsNotFound_WithoutTheDatabase(long id)
    {
        var result = await UseCase().ExecuteAsync(Agenda, AdminField.OfWhole(id), default);

        result.ShouldBeOfType<SendTestNotificationResult.UserNotFound>();
        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("text")]
    [InlineData("bool")]
    [InlineData("other")]
    public async Task UserIdOfAnotherType_IsInvalid(string kind)
    {
        var field = kind switch
        {
            "text" => AdminField.OfText("7"),
            "bool" => AdminField.OfBoolean(true),
            _ => AdminField.OtherValue(true),
        };

        (await UseCase().ExecuteAsync(Agenda, field, default)).ShouldBeOfType<SendTestNotificationResult.InvalidUserId>();
    }

    [Fact]
    public async Task ExistingUserWithUnknownType_Is400TipoInvalido()
    {
        var result = await UseCase().ExecuteAsync(AdminField.OfText("x"), AdminField.OfWhole(7), default);

        result.ShouldBeOfType<SendTestNotificationResult.InvalidType>();
    }

    [Theory]
    [InlineData("number")]
    [InlineData("bool")]
    public async Task TypeOfAnotherJsonKind_IsInvalidType(string kind)
    {
        var type = kind == "number" ? AdminField.OfWhole(1) : AdminField.OfBoolean(true);

        (await UseCase().ExecuteAsync(type, AdminField.OfWhole(7), default)).ShouldBeOfType<SendTestNotificationResult.InvalidType>();
    }

    [Fact]
    public async Task Agenda_WithoutGoogle_IsNotConnected_AndNothingIsSent()
    {
        _accounts.IsConnectedAsync(7, Arg.Any<CancellationToken>()).Returns(false);

        var result = await UseCase().ExecuteAsync(Agenda, AdminField.OfWhole(7), default);

        result.ShouldBeOfType<SendTestNotificationResult.NotConnected>();
        await _bot.DidNotReceiveWithAnyArgs().SendTextAsync(default!, default!, default);
    }

    [Fact]
    public async Task DefaultsAreAgendaAndUserOne()
    {
        _repository.FindUserAsync(1, Arg.Any<CancellationToken>()).Returns(new TestUser("Admin", "5511000000000"));
        _accounts.IsConnectedAsync(1, Arg.Any<CancellationToken>()).Returns(true);
        _calendar.GetEventsInRangeAsync(1, default, default, default).ReturnsForAnyArgs([]);

        var result = await UseCase().ExecuteAsync(AdminField.Absent, AdminField.NullValue, default);

        var sent = result.ShouldBeOfType<SendTestNotificationResult.Sent>();
        sent.Type.ShouldBe("agenda");
        sent.UserId.ShouldBe(1);
    }

    [Fact]
    public async Task Agenda_FormatsTheMessage_WithFiveLinesAndTheRest()
    {
        var events = Enumerable.Range(1, 7).Select(i => Event($"Evento {i}")).ToList();
        _calendar.GetEventsInRangeAsync(7, new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 30), Arg.Any<CancellationToken>())
            .Returns(events);

        var result = await UseCase().ExecuteAsync(Agenda, AdminField.OfWhole(7), default);

        var sent = result.ShouldBeOfType<SendTestNotificationResult.Sent>();
        sent.UserName.ShouldBe("Ana");
        sent.WhatsappNumber.ShouldBe("5511999990001");
        SentMessage().ShouldBe(
            "🧪 *TESTE - Agenda de Hoje*\n\n📅 7 evento(s) encontrado(s)\n\n1. Evento 1\n2. Evento 2\n3. Evento 3\n4. Evento 4\n5. Evento 5\n\n... e mais 2");
        await _bot.Received(1).SendTextAsync("5511999990001", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Agenda_WithFewEvents_HasNoTail()
    {
        _calendar.GetEventsInRangeAsync(7, default, default, default).ReturnsForAnyArgs([Event("Dentista")]);

        await UseCase().ExecuteAsync(Agenda, AdminField.OfWhole(7), default);

        SentMessage().ShouldBe("🧪 *TESTE - Agenda de Hoje*\n\n📅 1 evento(s) encontrado(s)\n\n1. Dentista\n");
    }

    [Fact]
    public async Task Bills_UseTomorrowsDayOfMonth_Limit5_AndPtBrMoney()
    {
        _repository.ListBillsForDayAsync(7, 31, 5, Arg.Any<CancellationToken>()).Returns(
        [
            new TestBill("Aluguel", 1234.5m),
            new TestBill("Internet", null),
        ]);

        var result = await UseCase().ExecuteAsync(Bills, AdminField.OfWhole(7), default);

        result.ShouldBeOfType<SendTestNotificationResult.Sent>().Type.ShouldBe("contas");
        SentMessage().ShouldBe("🧪 *TESTE - Contas de Amanhã*\n\n💰 2 conta(s) encontrada(s)\n\n1. Aluguel: R$ 1.234,50\n2. Internet: R$ 0,00\n");
        await _accounts.DidNotReceiveWithAnyArgs().IsConnectedAsync(default, default);
    }

    [Fact]
    public async Task Bills_TomorrowCrossesTheMonth_UsesTheNextMonthsDay()
    {
        _clock.Today.Returns(new DateOnly(2026, 2, 28));
        _repository.ListBillsForDayAsync(7, 1, 5, Arg.Any<CancellationToken>()).Returns([new TestBill("Escola", 10m)]);

        await UseCase().ExecuteAsync(Bills, AdminField.OfWhole(7), default);

        SentMessage().ShouldContain("1. Escola: R$ 10,00");
    }

    [Fact]
    public async Task BotFalse_IsSendFailed()
    {
        _calendar.GetEventsInRangeAsync(7, default, default, default).ReturnsForAnyArgs([]);
        _bot.SendTextAsync(default!, default!, default).ReturnsForAnyArgs(false);

        (await UseCase().ExecuteAsync(Agenda, AdminField.OfWhole(7), default)).ShouldBeOfType<SendTestNotificationResult.SendFailed>();
    }

    [Fact]
    public async Task UserWithoutNumber_PassesAnEmptyNumberToTheBot()
    {
        _repository.FindUserAsync(7, Arg.Any<CancellationToken>()).Returns(new TestUser("Ana", null));
        _calendar.GetEventsInRangeAsync(7, default, default, default).ReturnsForAnyArgs([]);
        _bot.SendTextAsync(string.Empty, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        (await UseCase().ExecuteAsync(Agenda, AdminField.OfWhole(7), default)).ShouldBeOfType<SendTestNotificationResult.SendFailed>();
    }
}

public class SendTestMonthlyReportTests
{
    private readonly IMonthlyReportJob _job = Substitute.For<IMonthlyReportJob>();

    [Theory]
    [InlineData("XX")]
    [InlineData("")]
    [InlineData("inicio_mes")]
    [InlineData(" INICIO_MES")]
    public async Task InvalidMoment_DoesNotCallThePort(string moment)
    {
        var result = await new SendTestMonthlyReport(_job).ExecuteAsync(5, moment, default);

        result.ShouldBeOfType<SendTestMonthlyReportResult.InvalidMoment>();
        _job.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, MonthlyMoment.InicioMes)]
    [InlineData("INICIO_MES", MonthlyMoment.InicioMes)]
    [InlineData("FIM_MES", MonthlyMoment.FimMes)]
    public async Task ValidMoment_CallsThePortWithTheUserAndMoment(string? moment, MonthlyMoment expected)
    {
        var report = new ManualReportResult(false, "Usuário não encontrado", 5, null, false, false, null);
        _job.SendManualAsync(5, expected, Arg.Any<CancellationToken>()).Returns(report);

        var result = await new SendTestMonthlyReport(_job).ExecuteAsync(5, moment, default);

        result.ShouldBeOfType<SendTestMonthlyReportResult.Done>().Report.ShouldBe(report);
    }
}

public class SendTestDailyBriefingTests
{
    private readonly IAdminTestDataRepository _repository = Substitute.For<IAdminTestDataRepository>();
    private readonly IDailyBriefingJob _job = Substitute.For<IDailyBriefingJob>();

    public SendTestDailyBriefingTests() =>
        _repository.FindUserAsync(7, Arg.Any<CancellationToken>()).Returns(new TestUser("Ana", "5511999990001"));

    private SendTestDailyBriefing UseCase() => new(_repository, _job);

    [Fact]
    public async Task UnknownUser_IsNotFound_WithTheId()
    {
        _repository.FindUserAsync(99, Arg.Any<CancellationToken>()).Returns((TestUser?)null);

        var result = await UseCase().ExecuteAsync(AdminField.OfWhole(99), default);

        result.ShouldBeOfType<SendTestDailyBriefingResult.UserNotFound>().UserId.ShouldBe(99);
        _job.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task HugeId_IsNotFound_WithoutTheDatabase()
    {
        var result = await UseCase().ExecuteAsync(AdminField.OfWhole(9_999_999_999), default);

        result.ShouldBeOfType<SendTestDailyBriefingResult.UserNotFound>().UserId.ShouldBe(9_999_999_999);
        _repository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task IdOfAnotherType_IsInvalid() =>
        (await UseCase().ExecuteAsync(AdminField.OfText("7"), default)).ShouldBeOfType<SendTestDailyBriefingResult.InvalidUserId>();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UserWithoutWhatsapp_Is400_AndThePortIsNotCalled(string? number)
    {
        _repository.FindUserAsync(7, Arg.Any<CancellationToken>()).Returns(new TestUser("Ana", number));

        var result = await UseCase().ExecuteAsync(AdminField.OfWhole(7), default);

        result.ShouldBeOfType<SendTestDailyBriefingResult.NoWhatsapp>().UserId.ShouldBe(7);
        _job.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task DefaultUserIsOne()
    {
        _repository.FindUserAsync(1, Arg.Any<CancellationToken>()).Returns(new TestUser("Admin", "5511000000000"));
        _job.SendTestAsync(1, Arg.Any<CancellationToken>()).Returns(new DailyBriefingTestResult(DailyBriefingTestOutcome.Sent, 0, "oi"));

        var result = await UseCase().ExecuteAsync(AdminField.Absent, default);

        result.ShouldBeOfType<SendTestDailyBriefingResult.Sent>().UserId.ShouldBe(1);
    }

    [Theory]
    [InlineData(DailyBriefingTestOutcome.PrepareFailed, typeof(SendTestDailyBriefingResult.PrepareFailed))]
    [InlineData(DailyBriefingTestOutcome.EmptyMessage, typeof(SendTestDailyBriefingResult.EmptyMessage))]
    [InlineData(DailyBriefingTestOutcome.SendFailed, typeof(SendTestDailyBriefingResult.SendFailed))]
    public async Task PortFailures_MapToTheirResults(DailyBriefingTestOutcome outcome, Type expected)
    {
        _job.SendTestAsync(7, Arg.Any<CancellationToken>()).Returns(new DailyBriefingTestResult(outcome, 0, null));

        (await UseCase().ExecuteAsync(AdminField.OfWhole(7), default)).ShouldBeOfType(expected);
    }

    [Fact]
    public async Task Sent_CarriesTheNumberTheEventsAndThePreview()
    {
        _job.SendTestAsync(7, Arg.Any<CancellationToken>()).Returns(new DailyBriefingTestResult(DailyBriefingTestOutcome.Sent, 3, "Bom dia!"));

        var sent = (await UseCase().ExecuteAsync(AdminField.OfWhole(7), default)).ShouldBeOfType<SendTestDailyBriefingResult.Sent>();

        sent.WhatsappNumber.ShouldBe("5511999990001");
        sent.TotalEvents.ShouldBe(3);
        sent.Preview.ShouldBe("Bom dia!");
    }

    [Fact]
    public void Preview_Exactly200_HasNoEllipsis() =>
        SendTestDailyBriefing.Preview(new string('a', 200)).ShouldBe(new string('a', 200));

    [Fact]
    public void Preview_Over200_CutsAt200_AndAddsEllipsis() =>
        SendTestDailyBriefing.Preview(new string('a', 201)).ShouldBe(new string('a', 200) + "...");

    [Fact]
    public void Preview_CountsCodePoints_NotUtf16Units()
    {
        // 200 emojis = 400 unidades UTF-16 mas 200 pontos de código (como o len() do Python): não corta.
        var emojis = string.Concat(Enumerable.Repeat("😀", 200));
        SendTestDailyBriefing.Preview(emojis).ShouldBe(emojis);

        var more = emojis + "x";
        SendTestDailyBriefing.Preview(more).ShouldBe(emojis + "...");
    }
}

public class UnavailableJobPortsTests
{
    [Fact]
    public async Task EveryUnavailablePortThrowsFeatureNotAvailable()
    {
        await Should.ThrowAsync<FeatureNotAvailableException>(() => new UnavailableScheduleMotor().RunAsync(default));
        await Should.ThrowAsync<FeatureNotAvailableException>(() => new UnavailableBillsNotificationJob().RunAsync(default));
        await Should.ThrowAsync<FeatureNotAvailableException>(() => new UnavailableDailyBriefingJob().RunForCurrentMinuteAsync(default));
        await Should.ThrowAsync<FeatureNotAvailableException>(() => new UnavailableDailyBriefingJob().SendTestAsync(1, default));
        await Should.ThrowAsync<FeatureNotAvailableException>(() => new UnavailableMonthlyReportJob().RunAsync(MonthlyMoment.FimMes, default));
        await Should.ThrowAsync<FeatureNotAvailableException>(() => new UnavailableMonthlyReportJob().SendManualAsync(1, MonthlyMoment.FimMes, default));
    }
}
