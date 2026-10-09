using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Jobs;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using MeuSecretario.Infrastructure.Jobs;

namespace MeuSecretario.UnitTests.Application.Jobs;

public class JobRunnerTests
{
    private sealed class FakeJob(Func<CancellationToken, Task> body, string name = "TESTE") : IScheduledJob
    {
        public string Name => name;

        public string LockName => name;

        public Task ExecuteAsync(CancellationToken cancellationToken) => body(cancellationToken);
    }

    private readonly JobsHarness _h = new();

    [Fact]
    [Trait("AC", "013-03")]
    public async Task Run_FreeLock_HoldsTheKeyWhileRunning_AndReleasesIt()
    {
        string? during = null;
        TimeSpan? ttl = null;
        var job = new FakeJob(_ =>
        {
            during = _h.Cache.RawValue(CacheKeys.JobLock("TESTE"));
            ttl = _h.Cache.TtlOf(CacheKeys.JobLock("TESTE"));
            return Task.CompletedTask;
        });

        var outcome = await _h.Runner.RunAsync(job, CancellationToken.None);

        outcome.ShouldBe(JobRunOutcome.Completed);
        during.ShouldBe("running");
        ttl!.Value.TotalSeconds.ShouldBeInRange(299, 300);
        _h.Cache.RawValue(CacheKeys.JobLock("TESTE")).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "013-03")]
    public async Task Run_LockTaken_SkipsWithoutRunningOrDeletingTheOtherKey()
    {
        await _h.Cache.SetIfNotExistsAsync(CacheKeys.JobLock("TESTE"), "outro", TimeSpan.FromMinutes(5));
        var ran = false;

        var outcome = await _h.Runner.RunAsync(new FakeJob(_ => { ran = true; return Task.CompletedTask; }), CancellationToken.None);

        outcome.ShouldBe(JobRunOutcome.SkippedLocked);
        ran.ShouldBeFalse();
        _h.Cache.RawValue(CacheKeys.JobLock("TESTE")).ShouldBe("outro");
        _h.Log.Lines.ShouldContain(l => l.Contains("já em execução", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-03")]
    public async Task Run_JobThrows_ReleasesTheLock_AndNeverPropagates()
    {
        var outcome = await _h.Runner.RunAsync(new FakeJob(_ => throw new InvalidOperationException("segredo-123")), CancellationToken.None);

        outcome.ShouldBe(JobRunOutcome.Failed);
        _h.Cache.RawValue(CacheKeys.JobLock("TESTE")).ShouldBeNull();
        _h.Log.Lines.ShouldContain(l => l.StartsWith("ERROR", StringComparison.Ordinal));
        _h.Log.Lines.ShouldNotContain(l => l.Contains("segredo-123", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-04")]
    public async Task Run_RedisDown_RunsWithoutLock_AndWarns()
    {
        _h.Cache.Unavailable = true;
        var ran = false;

        var outcome = await _h.Runner.RunAsync(new FakeJob(_ => { ran = true; return Task.CompletedTask; }), CancellationToken.None);

        outcome.ShouldBe(JobRunOutcome.Completed);
        ran.ShouldBeTrue();
        _h.Log.Lines.ShouldContain(l => l.StartsWith("WARNING", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-02")]
    public void Schedules_HaveTheSevenJobsWithTheOfeliaCrons()
    {
        var all = JobSchedules.All.ToDictionary(s => s.LockName);

        all.Count.ShouldBe(7);
        all["RESUMO-MATINAL"].Cron.ShouldBe("0 * * * * ?"); // desvio 136: por minuto
        all["RELATORIO-INICIO-MES"].Cron.ShouldBe("0 0 8 1 * ?");
        all["RELATORIO-FIM-MES"].Cron.ShouldBe("0 0 23 * * ?");
        all["CHECKIN-NOTURNO"].Cron.ShouldBe("0 * 18-23 * * ?"); // desvio 136: por minuto, das 18h às 23h59
        all["ALERTAS-TAREFAS"].Cron.ShouldBe("0 * * * * ?");
        all["INVOICE-PROCESSOR"].Cron.ShouldBe("0 1 0 * * ?");
        all["motor-agendamentos"].Cron.ShouldBe("0 5 0 * * ?");
    }

    [Theory]
    [Trait("AC", "013-02")]
    [InlineData("RESUMO-MATINAL", "2026-03-18T06:30:00-03:00", "2026-03-18T06:31:00-03:00")]
    [InlineData("RESUMO-MATINAL", "2026-03-18T07:29:30-03:00", "2026-03-18T07:30:00-03:00")]
    [InlineData("INVOICE-PROCESSOR", "2026-03-18T12:00:00-03:00", "2026-03-19T00:01:00-03:00")]
    [InlineData("RELATORIO-INICIO-MES", "2026-03-18T12:00:00-03:00", "2026-04-01T08:00:00-03:00")]
    [InlineData("RELATORIO-FIM-MES", "2026-03-18T23:00:00-03:00", "2026-03-19T23:00:00-03:00")]
    [InlineData("CHECKIN-NOTURNO", "2026-03-18T18:30:00-03:00", "2026-03-18T18:31:00-03:00")]
    [InlineData("CHECKIN-NOTURNO", "2026-03-18T19:14:10-03:00", "2026-03-18T19:15:00-03:00")]
    [InlineData("CHECKIN-NOTURNO", "2026-03-18T17:59:30-03:00", "2026-03-18T18:00:00-03:00")]
    [InlineData("CHECKIN-NOTURNO", "2026-03-18T23:59:00-03:00", "2026-03-19T18:00:00-03:00")]
    [InlineData("ALERTAS-TAREFAS", "2026-03-18T12:00:10-03:00", "2026-03-18T12:01:00-03:00")]
    [InlineData("motor-agendamentos", "2026-03-18T12:00:00-03:00", "2026-03-19T00:05:00-03:00")]
    public void Schedules_FireInTheSaoPauloTimeZone(string job, string after, string expected)
    {
        var zone = _h.Clock.TimeZone;
        var schedule = JobSchedules.All.Single(s => s.LockName == job);
        var trigger = (Quartz.ICronTrigger)JobsScheduler.BuildTrigger(schedule, zone);
        trigger.CronExpressionString.ShouldBe(schedule.Cron);
        trigger.TimeZone.Id.ShouldBe(zone.Id);
        trigger.MisfireInstruction.ShouldBe(Quartz.CronTriggerMisfireInstruction.DoNothing);

        var next = new Quartz.CronExpression(schedule.Cron, zone).GetNextValidTimeAfter(DateTimeOffset.Parse(after, System.Globalization.CultureInfo.InvariantCulture));

        next.ShouldBe(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }
}

public class TaskAlertsJobTests
{
    private readonly JobsHarness _h = new();
    private readonly ICalendarAlertConfigRepository _repository = Substitute.For<ICalendarAlertConfigRepository>();
    private readonly ICalendarClient _calendar = Substitute.For<ICalendarClient>();

    private TaskAlertsJob Job() => new(
        _repository, new CalendarAlertService(_calendar, _h.Cache, _h.Bot, _h.Clock, Substitute.For<ICalendarAlertDiagnostics>()), _h.Log);

    [Fact]
    [Trait("AC", "013-10")]
    public async Task Execute_NoUsers_DoesNothing()
    {
        _repository.ListUsersWithActiveAlertsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await Job().ExecuteAsync(CancellationToken.None);

        await _calendar.DidNotReceiveWithAnyArgs().ListSelectedCalendarsAsync(default, default);
        _h.Log.Lines.ShouldContain(l => l.Contains("Nenhum usuário com alertas ativos", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-10")]
    public async Task Execute_CallsTheServiceForEachUserInOrder_AndSumsTheSentAlerts()
    {
        _repository.ListUsersWithActiveAlertsAsync(Arg.Any<CancellationToken>())
            .Returns([new ActiveAlertUser(1, "5511", 5), new ActiveAlertUser(2, "5522", 10)]);
        _calendar.ListSelectedCalendarsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([new CalendarInfo("primary", "P", true)]);
        var start = "2026-03-18T18:05:00-03:00";
        _calendar.ListEventsAsync(1, Arg.Any<ListEventsQuery>(), Arg.Any<CancellationToken>())
            .Returns([new CalendarEvent("e1", "Reunião", start, start, "", "", false, null)]);
        _calendar.ListEventsAsync(2, Arg.Any<ListEventsQuery>(), Arg.Any<CancellationToken>()).Returns([]);

        await Job().ExecuteAsync(CancellationToken.None);

        Received.InOrder(() =>
        {
            _calendar.ListSelectedCalendarsAsync(1, Arg.Any<CancellationToken>());
            _calendar.ListSelectedCalendarsAsync(2, Arg.Any<CancellationToken>());
        });
        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.Contains("Faltam 5 min", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        _h.Log.Lines.ShouldContain(l => l.Contains("Total de alertas enviados: 1", StringComparison.Ordinal));
    }
}

public class InvoiceProcessorJobTests
{
    private readonly JobsHarness _h = new();
    private readonly IInvoiceJobRepository _repository = Substitute.For<IInvoiceJobRepository>();
    private readonly IInvoiceService _invoices = Substitute.For<IInvoiceService>();
    private readonly IInvoiceScheduleProcessor _schedules = Substitute.For<IInvoiceScheduleProcessor>();

    public InvoiceProcessorJobTests()
    {
        _repository.CloseExpiredAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _repository.ListOpenAsync(Arg.Any<CancellationToken>()).Returns([]);
        _repository.ListDueSoonAsync(Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private InvoiceProcessorJob Job() => new(_repository, _h.Bot, _h.Cache, _h.Clock, _invoices, _schedules, _h.Log);

    private static ClosedInvoice Closed(int id = 5) => new(id, 9, "Nubank", new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 17), 1234.5, 3, "5511");

    [Fact]
    [Trait("AC", "013-50")]
    public async Task Close_UsesTodayFromTheClock()
    {
        await Job().ExecuteAsync(CancellationToken.None);

        await _repository.Received(1).CloseExpiredAsync(new DateOnly(2026, 3, 18), Arg.Any<CancellationToken>());
        await _repository.Received(1).ListDueSoonAsync(new DateOnly(2026, 3, 21), 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-51")]
    public async Task ClosedInvoice_SendsOnce_AndRemembersForSevenDaysOnlyWhenSent()
    {
        _repository.CloseExpiredAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Closed()]);

        await Job().ExecuteAsync(CancellationToken.None);
        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.StartsWith("💳 *FATURA FECHADA*", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        _h.Cache.RawValue(CacheKeys.InvoiceClosed(5)).ShouldBe("true");
        _h.Cache.TtlOf(CacheKeys.InvoiceClosed(5))!.Value.TotalDays.ShouldBeInRange(6.9, 7);
    }

    [Fact]
    [Trait("AC", "013-51")]
    public async Task ClosedInvoice_FailedSend_DoesNotRemember()
    {
        _repository.CloseExpiredAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Closed()]);
        _h.Bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await Job().ExecuteAsync(CancellationToken.None);

        _h.Cache.RawValue(CacheKeys.InvoiceClosed(5)).ShouldBeNull();
        _h.Log.Lines.ShouldContain(l => l.StartsWith("WARNING", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-52")]
    public async Task ClosedInvoice_EnsuresTheCurrentInvoice_AndSurvivesItsFailure()
    {
        _repository.CloseExpiredAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Closed(1), Closed(2)]);
        _invoices.EnsureCurrentAsync(3, 9, Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("x"));

        await Job().ExecuteAsync(CancellationToken.None);

        await _invoices.Received(2).EnsureCurrentAsync(3, 9, Arg.Any<CancellationToken>());
        await _h.Bot.Received(2).SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-55")]
    public async Task Populate_RunsForEveryOpenInvoice_AndAnErrorDoesNotStopTheOthers()
    {
        _repository.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new OpenInvoiceRef(1, 10, new DateOnly(2026, 4, 10), "A"),
            new OpenInvoiceRef(2, 11, new DateOnly(2026, 4, 15), "B"),
        ]);
        _schedules.ProcessAsync(1, 10, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("x"));
        _schedules.ProcessAsync(2, 11, new DateOnly(2026, 4, 15), Arg.Any<CancellationToken>())
            .Returns(new InvoiceScheduleStats(3, 2, 1, 0, []));

        await Job().ExecuteAsync(CancellationToken.None);

        await _schedules.Received(1).ProcessAsync(2, 11, new DateOnly(2026, 4, 15), Arg.Any<CancellationToken>());
        _h.Log.Lines.ShouldContain(l => l.Contains("3 agendamentos processados (Fixos: 2, Parcelados: 1, Lembretes: 0)", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-53")]
    public async Task DueSoon_SendsOnce_AndRemembersTheWarning()
    {
        _repository.ListDueSoonAsync(Arg.Any<DateOnly>(), 3, Arg.Any<CancellationToken>())
            .Returns([new DueSoonInvoice(8, 9, "Inter", new DateOnly(2026, 3, 21), 10, 3, "5533", 3)]);

        await Job().ExecuteAsync(CancellationToken.None);
        await Job().ExecuteAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5533", Arg.Is<string>(m => m.Contains("em 3 dias (21/03/2026)", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        _h.Cache.RawValue(CacheKeys.InvoiceDueWarning3d(8)).ShouldBe("true");
    }
}

public class ScheduleMotorTests
{
    private readonly JobsHarness _h = new();
    private readonly IScheduleExecutionRepository _repository = Substitute.For<IScheduleExecutionRepository>();
    private readonly IScheduleExecutor _executor = Substitute.For<IScheduleExecutor>();

    public ScheduleMotorTests()
    {
        _executor.ExecuteAsync(Arg.Any<ScheduleExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ScheduleExecutionOutcome.Posted(1, "x", -1m, null));
    }

    private ScheduleMotor Motor() => new(_repository, _executor, _h.Bot, _h.Cache, _h.Clock, _h.Runner, _h.Log);

    private static ScheduleInfo Schedule(
        int id, string kind = ScheduleKinds.Fixed, int day = 18, int notify = 0, decimal? amount = 100m, string whatsapp = "5511") =>
        new(id, 2, 3, 4, "Luz", amount, kind, "MENSAL", day, null, null, 0, notify, "Despesa Essencial", "Conta Corrente", whatsapp);

    [Fact]
    [Trait("AC", "013-60")]
    public async Task Run_ExecutesFixedAndInstallmentsOfTodayOnly()
    {
        _repository.ListMotorCandidatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
        [
            Schedule(1), Schedule(2, ScheduleKinds.Installment), Schedule(3, day: 19),
        ]);

        var result = await Motor().RunAsync(CancellationToken.None);

        result.ShouldBe(MotorRunResult.Executed);
        await _executor.Received(1).ExecuteAsync(new ScheduleExecutionRequest(1, new DateOnly(2026, 3, 18)), Arg.Any<CancellationToken>());
        await _executor.Received(1).ExecuteAsync(new ScheduleExecutionRequest(2, new DateOnly(2026, 3, 18)), Arg.Any<CancellationToken>());
        await _executor.DidNotReceive().ExecuteAsync(new ScheduleExecutionRequest(3, new DateOnly(2026, 3, 18)), Arg.Any<CancellationToken>());
        _h.Cache.RawValue(CacheKeys.JobLock("motor-agendamentos")).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task Run_OnTheLastDayOfAShortMonth_ExecutesSchedulesOfDay29To31()
    {
        // desvio 134: 30/04/2026 é o último dia de abril.
        _h.SetToday(2026, 4, 30);
        _repository.ListMotorCandidatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
        [
            Schedule(1, day: 31), Schedule(2, ScheduleKinds.Installment, day: 30), Schedule(3, day: 10),
        ]);

        await Motor().RunAsync(CancellationToken.None);

        var april30 = new DateOnly(2026, 4, 30);
        await _executor.Received(1).ExecuteAsync(new ScheduleExecutionRequest(1, april30), Arg.Any<CancellationToken>());
        await _executor.Received(1).ExecuteAsync(new ScheduleExecutionRequest(2, april30), Arg.Any<CancellationToken>());
        await _executor.DidNotReceive().ExecuteAsync(new ScheduleExecutionRequest(3, april30), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task Run_BeforeTheLastDay_DoesNotExecuteDay31()
    {
        _h.SetToday(2026, 4, 29);
        _repository.ListMotorCandidatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Schedule(1, day: 31)]);

        await Motor().RunAsync(CancellationToken.None);

        await _executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "013-66")]
    public async Task Run_LockTaken_ReturnsAlreadyRunning_WithoutProcessing()
    {
        await _h.Cache.SetIfNotExistsAsync(CacheKeys.JobLock("motor-agendamentos"), "running", TimeSpan.FromMinutes(5));

        var result = await Motor().RunAsync(CancellationToken.None);

        result.ShouldBe(MotorRunResult.AlreadyRunning);
        await _repository.DidNotReceiveWithAnyArgs().ListMotorCandidatesAsync(default, default);
    }

    [Fact]
    [Trait("AC", "013-65")]
    public async Task Run_AnErrorInOneSchedule_DoesNotStopTheOthers()
    {
        _repository.ListMotorCandidatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([Schedule(1), Schedule(2)]);
        _executor.ExecuteAsync(new ScheduleExecutionRequest(1, new DateOnly(2026, 3, 18)), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("x"));

        await Motor().RunAsync(CancellationToken.None);

        await _executor.Received(1).ExecuteAsync(new ScheduleExecutionRequest(2, new DateOnly(2026, 3, 18)), Arg.Any<CancellationToken>());
        _h.Log.Lines.ShouldContain(l => l.Contains("ERRO ao processar Agendamento ID 1", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "013-63")]
    public async Task Reminder_FiresOnDueMinusNotifyDays_Once()
    {
        // vence dia 21, aviso 3 dias antes => hoje (18).
        _repository.ListMotorCandidatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([Schedule(7, ScheduleKinds.VariableReminder, day: 21, notify: 3), Schedule(8, ScheduleKinds.VariableReminder, day: 25, notify: 3)]);

        await Motor().RunAsync(CancellationToken.None);
        await Motor().RunAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Is<string>(m => m.Contains("vence em 3 dias (no dia 21)", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await _executor.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        _h.Cache.TtlOf("motor_lembrete:7:2026-03-21")!.Value.TotalHours.ShouldBeInRange(47, 48);
    }

    [Fact]
    [Trait("AC", "013-63")]
    public async Task Reminder_FailedSend_ReleasesTheKeyForTheNextRun()
    {
        _repository.ListMotorCandidatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([Schedule(7, ScheduleKinds.VariableReminder, day: 18, notify: 0)]);
        _h.Bot.SendTextAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false, true);

        await Motor().RunAsync(CancellationToken.None);
        await Motor().RunAsync(CancellationToken.None);

        await _h.Bot.Received(2).SendTextAsync("5511", Arg.Any<string>(), Arg.Any<CancellationToken>());
        _h.Cache.RawValue("motor_lembrete:7:2026-03-18").ShouldNotBeNull();
    }

    [Fact]
    [Trait("AC", "013-63")]
    public async Task Reminder_WithoutRedis_StillSends()
    {
        _h.Cache.Unavailable = true;
        _repository.ListMotorCandidatesAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([Schedule(7, ScheduleKinds.VariableReminder, day: 18, notify: 0)]);

        await Motor().RunAsync(CancellationToken.None);

        await _h.Bot.Received(1).SendTextAsync("5511", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
