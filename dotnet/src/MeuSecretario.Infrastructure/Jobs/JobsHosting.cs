using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Application.Ops;
using MeuSecretario.Infrastructure.CalendarAlerts;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.Infrastructure.Ops;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace MeuSecretario.Infrastructure.Jobs;

internal static partial class JobsLog
{
    [LoggerMessage(EventId = 13001, Message = "{Line}")]
    public static partial void Line(ILogger logger, LogLevel level, string line);

    [LoggerMessage(EventId = 13002, Level = LogLevel.Information, Message = "Jobs desligados (JOBS_ENABLED diferente de true): nenhum gatilho registrado.")]
    public static partial void Disabled(ILogger logger);

    [LoggerMessage(EventId = 13003, Level = LogLevel.Information, Message = "Jobs ligados: {Count} gatilhos no fuso {Zone}.")]
    public static partial void Enabled(ILogger logger, int count, string zone);
}

/// <summary><c>[AAAA-MM-DD HH:MM:SS] [NOME] NÍVEL: texto</c> no console e em <c>{LOG_DIR}/{nome}.log</c> (RN-013-03).</summary>
public sealed class JobLog(AppSettings settings, IClock clock, ILogger<JobLog> logger) : IJobLog
{
    private static readonly object Gate = new();

    public void Info(string job, string message) => Write(job, "INFO", LogLevel.Information, message);

    public void Warning(string job, string message) => Write(job, "WARNING", LogLevel.Warning, message);

    public void Failure(string job, string message) => Write(job, "ERROR", LogLevel.Error, message);

    private void Write(string job, string level, LogLevel logLevel, string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"[{clock.Now:yyyy-MM-dd HH:mm:ss}] [{job}] {level}: {message}");
        JobsLog.Line(logger, logLevel, line);
        if (!settings.LogToFile)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(settings.LogDirectory);
                File.AppendAllText(Path.Combine(settings.LogDirectory, job.ToLowerInvariant() + ".log"), line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Falha ao gravar o arquivo é ignorada (base_job.py).
        }
    }
}

/// <summary>Resolve o <see cref="IScheduledJob"/> pelo <c>LockName</c> num escopo novo e o executa pelo <see cref="JobRunner"/>.</summary>
[DisallowConcurrentExecution]
public sealed class ScheduledJobAdapter(IServiceScopeFactory scopes) : IJob
{
    public const string JobKey = "job";

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var key = context.MergedJobDataMap.GetString(JobKey);
        await using var scope = scopes.CreateAsyncScope();
        var job = scope.ServiceProvider.GetServices<IScheduledJob>().FirstOrDefault(j => j.LockName == key);
        if (job is null)
        {
            return;
        }

        await scope.ServiceProvider.GetRequiredService<JobRunner>().RunAsync(job, cancellationToken);
    }
}

/// <summary>
/// Liga o agendador só com <c>JOBS_ENABLED=true</c> (RN-013-04). Desligado, não registra gatilho nenhum. O Quartz roda em memória,
/// no fuso de São Paulo, com misfire <c>DoNothing</c> (RN-013-05).
/// </summary>
public sealed class JobsScheduler(
    ISchedulerFactory factory, IOptions<JobsOptions> options, IClock clock, ILogger<JobsScheduler> logger) : IHostedService
{
    private IScheduler? _scheduler;
    private bool _started;

    /// <summary>Gatilho cron no fuso informado, descartando ciclos perdidos (misfire <c>DoNothing</c>).</summary>
    public static ITrigger BuildTrigger(JobSchedule schedule, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        return TriggerBuilder.Create()
            .WithIdentity(schedule.LockName)
            .WithCronSchedule(schedule.Cron, x => x.InTimeZone(timeZone).WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .Build();
    }

    public static async Task ScheduleAllAsync(IScheduler scheduler, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        foreach (var schedule in JobSchedules.All)
        {
            var job = JobBuilder.Create<ScheduledJobAdapter>()
                .WithIdentity(schedule.LockName)
                .UsingJobData(ScheduledJobAdapter.JobKey, schedule.LockName)
                .Build();
            var trigger = BuildTrigger(schedule, timeZone);
            await scheduler.ScheduleJob(job, trigger, new ScheduleJobOptions(), cancellationToken);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            JobsLog.Disabled(logger);
            return;
        }

        _scheduler = await factory.GetScheduler(cancellationToken);
        await ScheduleAllAsync(_scheduler, clock.TimeZone, cancellationToken);
        await _scheduler.Start(cancellationToken);
        _started = true;
        JobsLog.Enabled(logger, JobSchedules.All.Count, JobSchedules.TimeZoneId);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_scheduler is { } scheduler && _started)
        {
            await scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken);
        }
    }
}

public static class JobsServiceCollectionExtensions
{
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IJobNotificationConfigRepository, JobNotificationConfigRepository>();
        services.TryAddSingleton<IBriefingDataRepository, BriefingDataRepository>();
        services.TryAddSingleton<IFinancialAlertsRepository, FinancialAlertsRepository>();
        services.TryAddSingleton<IBillsReminderRepository, BillsReminderRepository>();
        services.TryAddSingleton<ICheckinQueries, CheckinQueries>();
        services.TryAddSingleton<IInvoiceJobRepository, InvoiceJobRepository>();
        services.TryAddSingleton<IScheduleExecutionRepository, ScheduleExecutionRepository>();
        services.TryAddSingleton<IMonthlyReportRepository, MonthlyReportRepository>();
        services.TryAddSingleton<IChartRenderer, ScottPlotChartRenderer>();
        services.TryAddSingleton<JobLog>();
        services.TryAddSingleton<OpsAlertChannel>(sp => new OpsAlertChannel(sp.GetRequiredService<AppSettings>().OpsAlertWhatsapp.Length > 0));
        services.TryAddSingleton<IOpsAlerts>(sp => sp.GetRequiredService<OpsAlertChannel>());
        services.TryAddSingleton<IJobLog>(sp => new AlertingJobLog(sp.GetRequiredService<JobLog>(), sp.GetRequiredService<IOpsAlerts>()));
        services.AddHostedService(sp => new OpsAlertWorker(
            sp.GetRequiredService<OpsAlertChannel>(),
            sp.GetRequiredService<IBotClient>(),
            sp.GetRequiredService<AppSettings>().OpsAlertWhatsapp,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<OpsAlertWorker>>()));

        services.AddScoped<JobRunner>();
        services.AddScoped<IScheduleExecutor, ScheduleExecutor>();
        services.AddScoped<DailyBriefingService>();
        services.AddScoped<NightlyCheckinService>();

        services.AddScoped<TaskAlertsJob>();
        services.AddScoped<InvoiceProcessorJob>();
        services.AddScoped<ScheduleMotor>();
        services.AddScoped<DailyBriefingJob>();
        services.AddScoped<NightlyCheckinJob>();
        services.AddScoped<MonthlyReportJob>();
        services.AddScoped<StartOfMonthReportJob>();
        services.AddScoped<EndOfMonthReportJob>();
        services.AddScoped<BillsNotificationJob>();

        // Portas para a 009c.
        services.AddScoped<IScheduleMotor>(sp => sp.GetRequiredService<ScheduleMotor>());
        services.AddScoped<IDailyBriefingJob>(sp => sp.GetRequiredService<DailyBriefingJob>());
        services.AddScoped<IMonthlyReportJob>(sp => sp.GetRequiredService<MonthlyReportJob>());
        services.AddScoped<IBillsNotificationJob>(sp => sp.GetRequiredService<BillsNotificationJob>());

        // Os agendados.
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<TaskAlertsJob>());
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<InvoiceProcessorJob>());
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<ScheduleMotor>());
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<DailyBriefingJob>());
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<NightlyCheckinJob>());
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<StartOfMonthReportJob>());
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<EndOfMonthReportJob>());

        // Dependências da 014 usadas pelo job de alertas de tarefas (idempotente).
        services.AddCalendarAlertsInfrastructure();

        services.AddQuartz();
        services.AddHostedService<JobsScheduler>();
        return services;
    }
}
