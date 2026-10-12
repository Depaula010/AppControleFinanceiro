using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;

namespace MeuSecretario.Application.Jobs;

/// <summary>Um job agendado (spec 013 §4.1). O adaptador do Quartz só resolve por <see cref="Name"/> e chama o <see cref="JobRunner"/>.</summary>
public interface IScheduledJob
{
    /// <summary>Nome em maiúsculas usado em log.</summary>
    string Name { get; }

    /// <summary>Sufixo de <c>job_lock:{LockName}</c>.</summary>
    string LockName { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>Log dos jobs (<c>[hora] [NOME] NÍVEL: texto</c>); implementado na Infrastructure. Nunca receber número, texto de mensagem nem segredo.</summary>
public interface IJobLog
{
    void Info(string job, string message);

    void Warning(string job, string message);

    void Failure(string job, string message);
}

public sealed record JobSchedule(string Name, string LockName, string Cron);

/// <summary>Agenda dos jobs (spec 013 §2): cron do Quartz (6 campos com segundos) no fuso de São Paulo.</summary>
public static class JobSchedules
{
    public const string TimeZoneId = "America/Sao_Paulo";

    public const string MorningSummary = "RESUMO-MATINAL";
    public const string StartOfMonthReport = "RELATORIO-INICIO-MES";
    public const string EndOfMonthReport = "RELATORIO-FIM-MES";
    public const string NightlyCheckin = "CHECKIN-NOTURNO";
    public const string TaskAlerts = "ALERTAS-TAREFAS";
    public const string InvoiceProcessor = "INVOICE-PROCESSOR";
    public const string ScheduleMotor = "motor-agendamentos";

    public static IReadOnlyList<JobSchedule> All { get; } =
    [
        // Desvio 136: a cada minuto (o Python rodava só no minuto 0 e comparava o TIME por igualdade).
        new(MorningSummary, MorningSummary, "0 * * * * ?"),
        new(StartOfMonthReport, StartOfMonthReport, "0 0 8 1 * ?"),
        new(EndOfMonthReport, EndOfMonthReport, "0 0 23 * * ?"),
        new(NightlyCheckin, NightlyCheckin, "0 * 18-23 * * ?"),
        new(TaskAlerts, TaskAlerts, "0 * * * * ?"),
        new(InvoiceProcessor, InvoiceProcessor, "0 1 0 * * ?"),
        new(ScheduleMotor, ScheduleMotor, "0 5 0 * * ?"),
    ];
}

/// <summary>Configuração dos jobs. <c>JOBS_ENABLED=true</c> liga o agendador (padrão desligado; RN-013-04).</summary>
public sealed class JobsOptions
{
    public bool Enabled { get; set; }
}

public enum JobRunOutcome
{
    Completed,
    SkippedLocked,
    Failed,
}

/// <summary>Lock, erros e log de cada execução (<c>base_job.py</c>, RN-013-01 a 03). Nunca lança (exceto cancelamento).</summary>
public sealed class JobRunner(ICache cache, IClock clock, IJobLog log)
{
    public static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(300);

    private const string LockValue = "running";

    public async Task<JobRunOutcome> RunAsync(IScheduledJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var started = clock.UtcNow;
        log.Info(job.Name, "INICIANDO PROCESSAMENTO");
        var outcome = JobRunOutcome.Completed;
        try
        {
            var executed = await RunLockedAsync(job.Name, job.LockName, job.ExecuteAsync, cancellationToken);
            if (!executed)
            {
                outcome = JobRunOutcome.SkippedLocked;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Failure(job.Name, $"ERRO durante execução: {ex.GetType().Name}");
            outcome = JobRunOutcome.Failed;
        }
        finally
        {
            var seconds = (clock.UtcNow - started).TotalSeconds;
            if (outcome == JobRunOutcome.Failed)
            {
                log.Failure(job.Name, $"PROCESSAMENTO FINALIZADO COM ERROS. Duração: {seconds:F2} segundos");
            }
            else
            {
                log.Info(job.Name, $"PROCESSAMENTO CONCLUÍDO COM SUCESSO. Duração: {seconds:F2} segundos");
            }
        }

        return outcome;
    }

    /// <summary>
    /// Executa <paramref name="body"/> sob <c>job_lock:{lockName}</c>. <c>false</c> = outro processo detém o lock (nada roda).
    /// Redis fora = executa sem lock (fail-open). Exceções do corpo sobem depois de liberar o lock.
    /// </summary>
    public async Task<bool> RunLockedAsync(string jobName, string lockName, Func<CancellationToken, Task> body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        var key = CacheKeys.JobLock(lockName);
        var acquired = false;
        if (await cache.IsAvailableAsync(cancellationToken))
        {
            acquired = await cache.SetIfNotExistsAsync(key, LockValue, LockTtl, cancellationToken);
            if (!acquired)
            {
                log.Warning(jobName, "Job já em execução por outro processo, pulando...");
                return false;
            }

            log.Info(jobName, $"Lock adquirido (key: {key}, TTL: 5min)");
        }
        else
        {
            log.Warning(jobName, "Redis indisponível, executando sem lock distribuído");
        }

        try
        {
            await body(cancellationToken);
            return true;
        }
        finally
        {
            if (acquired)
            {
                await cache.DeleteAsync(key, CancellationToken.None);
                log.Info(jobName, $"Lock liberado (key: {key})");
            }
        }
    }
}
