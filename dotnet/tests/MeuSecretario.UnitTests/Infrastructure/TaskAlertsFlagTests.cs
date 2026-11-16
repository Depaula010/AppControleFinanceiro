#pragma warning disable CA2012 // o ValueTask do substituto é configurado, não aguardado
using NSubstitute;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Infrastructure.Jobs;
using Quartz;

namespace MeuSecretario.UnitTests.Infrastructure;

/// <summary>Desvio 278: <c>TASK_ALERTS_ENABLED</c> (padrão true) decide se o job de alertas de tarefas é agendado.</summary>
public class TaskAlertsFlagTests
{
    [Fact]
    public async Task ScheduleAll_WithTaskAlertsOff_SkipsOnlyThatJob()
    {
        var scheduler = Substitute.For<IScheduler>();
        var names = new List<string>();
        scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<ScheduleJobOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                names.Add(call.Arg<IJobDetail>().Key.Name);
                return new ValueTask<DateTimeOffset>(DateTimeOffset.UtcNow);
            });

        await JobsScheduler.ScheduleAllAsync(scheduler, TimeZoneInfo.Utc, CancellationToken.None, taskAlertsEnabled: false);

        names.ShouldNotContain(JobSchedules.TaskAlerts);
        names.Count.ShouldBe(JobSchedules.All.Count - 1);
    }

    [Fact]
    public async Task ScheduleAll_ByDefault_KeepsTheTaskAlertsJob()
    {
        var scheduler = Substitute.For<IScheduler>();
        var names = new List<string>();
        scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<ScheduleJobOptions>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                names.Add(call.Arg<IJobDetail>().Key.Name);
                return new ValueTask<DateTimeOffset>(DateTimeOffset.UtcNow);
            });

        await JobsScheduler.ScheduleAllAsync(scheduler, TimeZoneInfo.Utc, CancellationToken.None);

        names.ShouldContain(JobSchedules.TaskAlerts);
        names.Count.ShouldBe(JobSchedules.All.Count);
    }
}
