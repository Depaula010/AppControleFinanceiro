using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Application.Transactions;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace MeuSecretario.IntegrationTests.Jobs;

/// <summary>O agendador só liga com <c>JOBS_ENABLED=true</c>; as portas da 009c existem sempre (AC-013-01 e 81).</summary>
[Trait("Category", "Docker")]
public sealed class JobsHostTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private WebApplicationFactory<Program> Host(string? enabled) => postgres.Api.WithWebHostBuilder(builder =>
    {
        if (enabled is not null)
        {
            builder.UseSetting("JOBS_ENABLED", enabled);
        }
    });

    private static async Task<(bool Started, int Jobs, int Triggers)> InspectAsync(IServiceProvider services)
    {
        var scheduler = await services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var started = scheduler.Status == SchedulerStatus.Running;
        if (started)
        {
            await scheduler.PauseAll();
        }

        var jobs = await scheduler.QueryJobs(new JobQuery { Take = 100 });
        var triggers = await scheduler.QueryTriggers(new TriggerQuery { Take = 100 });
        return (started, jobs.Items.Count, triggers.Items.Count);
    }

    [Theory]
    [Trait("AC", "013-01")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("1")]
    [InlineData("yes")]
    public async Task WithoutJobsEnabled_NothingIsScheduled(string? value)
    {
        await using var factory = Host(value);

        var (started, jobs, triggers) = await InspectAsync(factory.Services);

        started.ShouldBeFalse();
        (jobs, triggers).ShouldBe((0, 0));
    }

    [Theory]
    [Trait("AC", "013-81")]
    [InlineData("true")]
    [InlineData("TRUE")]
    public async Task WithJobsEnabled_TheSchedulerStartsWithTheSevenTriggers_AndStopsCleanly(string value)
    {
        var factory = Host(value);

        var (started, jobs, triggers) = await InspectAsync(factory.Services);
        await factory.DisposeAsync();

        started.ShouldBeTrue();
        (jobs, triggers).ShouldBe((7, 7));
    }

    [Fact]
    [Trait("AC", "013-01")]
    public async Task Ports_AndJobs_AreResolvable_AndTheRealPopulatorWins()
    {
        await using var factory = Host(null);
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        provider.GetRequiredService<IScheduleMotor>().ShouldNotBeNull();
        provider.GetRequiredService<IBillsNotificationJob>().ShouldNotBeNull();
        provider.GetRequiredService<IDailyBriefingJob>().ShouldNotBeNull();
        provider.GetRequiredService<IMonthlyReportJob>().ShouldNotBeNull();
        provider.GetRequiredService<IChartRenderer>().ShouldNotBeNull();
        provider.GetServices<IScheduledJob>().Select(j => j.LockName).OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe([.. JobSchedules.All.Select(s => s.LockName).OrderBy(n => n, StringComparer.Ordinal)]);
        provider.GetRequiredService<IInvoiceSchedulePopulator>().ShouldBeOfType<MeuSecretario.Application.Automation.InvoiceSchedulePopulator>();
        provider.GetRequiredService<IInvoiceScheduleProcessor>().ShouldNotBeNull();
    }
}
