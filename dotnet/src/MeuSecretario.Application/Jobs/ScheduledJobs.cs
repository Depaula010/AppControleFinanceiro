using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Ops;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Application.Jobs;

/// <summary>Alertas de tarefas do Google Calendar, a cada minuto (<c>task_alerts.py</c>, spec 013 §4.2).</summary>
public sealed class TaskAlertsJob(ICalendarAlertConfigRepository repository, CalendarAlertService service, IJobLog log) : IScheduledJob
{
    public string Name => JobSchedules.TaskAlerts;

    public string LockName => JobSchedules.TaskAlerts;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var users = await repository.ListUsersWithActiveAlertsAsync(cancellationToken);
        if (users.Count == 0)
        {
            log.Info(Name, "Nenhum usuário com alertas ativos");
            return;
        }

        log.Info(Name, $"Processando {users.Count.ToString(CultureInfo.InvariantCulture)} usuário(s)");
        var total = 0;
        foreach (var user in users)
        {
            total += await service.ProcessAlertsForUserAsync(user.UserId, user.WhatsappNumber, user.MinutesBefore, cancellationToken);
        }

        log.Info(Name, $"Total de alertas enviados: {total.ToString(CultureInfo.InvariantCulture)}");
    }
}

/// <summary>A rotina única de execução de agendamento (desvio 011), exposta para o motor e para a 012 (confirmar contas do check-in).</summary>
public interface IScheduleExecutor
{
    Task<ScheduleExecutionOutcome> ExecuteAsync(ScheduleExecutionRequest request, CancellationToken cancellationToken);
}

public sealed class ScheduleExecutor(IScheduleExecutionRepository repository) : IScheduleExecutor
{
    public Task<ScheduleExecutionOutcome> ExecuteAsync(ScheduleExecutionRequest request, CancellationToken cancellationToken) =>
        repository.ExecuteAsync(request, cancellationToken);
}

/// <summary>Processador de faturas, 00:01 (<c>invoice_processor.py</c>, spec 013 §4.6).</summary>
public sealed class InvoiceProcessorJob(
    IInvoiceJobRepository repository,
    IBotClient bot,
    ICache cache,
    IClock clock,
    IInvoiceService invoiceService,
    IInvoiceScheduleProcessor schedules,
    IJobLog log) : IScheduledJob
{
    private static readonly TimeSpan DedupeTtl = TimeSpan.FromDays(7);

    public string Name => JobSchedules.InvoiceProcessor;

    public string LockName => JobSchedules.InvoiceProcessor;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await CloseInvoicesAsync(cancellationToken);
        await PopulateOpenInvoicesAsync(cancellationToken);
        await WarnDueSoonAsync(cancellationToken);
        log.Info(Name, "Processamento de faturas concluído!");
    }

    private async Task CloseInvoicesAsync(CancellationToken cancellationToken)
    {
        log.Info(Name, "Buscando faturas para fechar...");
        var closed = await repository.CloseExpiredAsync(clock.Today, cancellationToken);
        if (closed.Count == 0)
        {
            log.Info(Name, "Nenhuma fatura para fechar hoje");
            return;
        }

        log.Info(Name, $"Encontradas {closed.Count.ToString(CultureInfo.InvariantCulture)} fatura(s) para fechar");
        foreach (var invoice in closed)
        {
            var key = CacheKeys.InvoiceClosed(invoice.Id);
            if (!await cache.ExistsAsync(key, cancellationToken))
            {
                var sent = await bot.SendTextAsync(invoice.WhatsappNumber, InvoiceNotificationFormatter.Closed(invoice), cancellationToken);
                if (sent)
                {
                    await cache.SetJsonAsync(key, true, DedupeTtl, cancellationToken);
                    log.Info(Name, $"Notificação de fechamento enviada - Fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}");
                }
                else
                {
                    log.Warning(Name, $"Falha ao enviar notificação - Fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}");
                }
            }
            else
            {
                log.Info(Name, $"Notificação já enviada anteriormente - Fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}");
            }

            try
            {
                await invoiceService.EnsureCurrentAsync(invoice.UserId, invoice.AccountId, cancellationToken);
                log.Info(Name, $"Nova fatura garantida para o cartão {invoice.AccountId.ToString(CultureInfo.InvariantCulture)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Failure(Name, $"Erro ao criar nova fatura para o cartão {invoice.AccountId.ToString(CultureInfo.InvariantCulture)}: {ex.GetType().Name}");
            }
        }
    }

    private async Task PopulateOpenInvoicesAsync(CancellationToken cancellationToken)
    {
        log.Info(Name, "Verificando faturas abertas sem agendamentos...");
        var open = await repository.ListOpenAsync(cancellationToken);
        if (open.Count == 0)
        {
            log.Info(Name, "Nenhuma fatura aberta encontrada");
            return;
        }

        log.Info(Name, $"Encontradas {open.Count.ToString(CultureInfo.InvariantCulture)} fatura(s) aberta(s)");
        foreach (var invoice in open)
        {
            try
            {
                var stats = await schedules.ProcessAsync(invoice.Id, invoice.AccountId, invoice.DueDate, cancellationToken);
                if (stats.Processed > 0)
                {
                    log.Info(Name, $"Fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}: {stats.Processed.ToString(CultureInfo.InvariantCulture)} agendamentos processados " +
                        $"(Fixos: {stats.Fixed.ToString(CultureInfo.InvariantCulture)}, Parcelados: {stats.Installments.ToString(CultureInfo.InvariantCulture)}, Lembretes: {stats.Reminders.ToString(CultureInfo.InvariantCulture)})");
                }
                else
                {
                    log.Info(Name, $"Fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}: já processada");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Failure(Name, $"Erro ao processar fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}: {ex.GetType().Name}");
            }
        }
    }

    private async Task WarnDueSoonAsync(CancellationToken cancellationToken)
    {
        log.Info(Name, "Buscando faturas com vencimento próximo (3 dias)...");
        var target = clock.Today.AddDays(3);
        var invoices = await repository.ListDueSoonAsync(target, 3, cancellationToken);
        if (invoices.Count == 0)
        {
            log.Info(Name, "Nenhuma fatura vence em 3 dias");
            return;
        }

        foreach (var invoice in invoices)
        {
            var key = CacheKeys.InvoiceDueWarning3d(invoice.Id);
            if (await cache.ExistsAsync(key, cancellationToken))
            {
                log.Info(Name, $"Alerta já enviado - Fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}");
                continue;
            }

            var sent = await bot.SendTextAsync(invoice.WhatsappNumber, InvoiceNotificationFormatter.DueDateWarning(invoice), cancellationToken);
            if (sent)
            {
                await cache.SetJsonAsync(key, true, DedupeTtl, cancellationToken);
                log.Info(Name, $"Alerta de vencimento enviado - Fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}");
            }
            else
            {
                log.Warning(Name, $"Falha ao enviar alerta - Fatura #{invoice.Id.ToString(CultureInfo.InvariantCulture)}");
            }
        }
    }
}

/// <summary>Motor de agendamentos (<c>schedule_processor.py</c>, spec 013 §4.7; desvios 011, 012 e 013).</summary>
public sealed class ScheduleMotor(
    IScheduleExecutionRepository repository,
    IScheduleExecutor executor,
    IBotClient bot,
    ICache cache,
    IClock clock,
    JobRunner runner,
    IJobLog log,
    IOpsAlerts alerts) : IScheduleMotor, IScheduledJob
{
    private static readonly TimeSpan ReminderTtl = TimeSpan.FromHours(48);

    /// <summary>Quantos dias para trás o motor confere o que deveria ter lançado (desvio 262).</summary>
    public const int CatchUpDays = 3;

    public string Name => "MOTOR-AGENDAMENTOS";

    public string LockName => JobSchedules.ScheduleMotor;

    public async Task<MotorRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var executed = await runner.RunLockedAsync(Name, LockName, ProcessAsync, cancellationToken);
        return executed ? MotorRunResult.Executed : MotorRunResult.AlreadyRunning;
    }

    /// <summary>Corpo sem lock (o <see cref="JobRunner"/> já travou quando vem do agendador).</summary>
    public Task ExecuteAsync(CancellationToken cancellationToken) => ProcessAsync(cancellationToken);

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        log.Info(Name, "Início do processamento de agendamentos...");
        var today = clock.Today;
        var candidates = await repository.ListMotorCandidatesAsync(today, cancellationToken);
        log.Info(Name, $"{candidates.Count.ToString(CultureInfo.InvariantCulture)} agendamentos candidatos.");

        foreach (var schedule in candidates)
        {
            try
            {
                if (schedule.Kind == ScheduleKinds.VariableReminder)
                {
                    await RemindAsync(schedule, today, cancellationToken);
                }
                else if (ScheduleRules.MotorDayMatches(schedule, today))
                {
                    var outcome = await executor.ExecuteAsync(new ScheduleExecutionRequest(schedule.Id, today), cancellationToken);
                    log.Info(Name, $"Agendamento ID {schedule.Id.ToString(CultureInfo.InvariantCulture)}: {Describe(outcome)}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Failure(Name, $"ERRO ao processar Agendamento ID {schedule.Id.ToString(CultureInfo.InvariantCulture)}: {ex.GetType().Name}");
            }
        }

        await CatchUpAsync(today, cancellationToken);

        log.Info(Name, "Processamento de agendamentos finalizado.");
    }

    /// <summary>
    /// Desvio 262: o motor só lançava no dia exato; se o app estivesse fora do ar (ou o job falhasse) naquele dia, a conta nunca era
    /// lançada sozinha. Agora cada execução reconfere os últimos <see cref="CatchUpDays"/> dias: o executor é idempotente (competência,
    /// regra de "paga" e índice único), então repetir o que já foi lançado dá <c>Skipped</c> e só o que ficou para trás é lançado,
    /// na data do próprio vencimento. Se recuperar algo, avisa o dono: significa que um dia foi perdido.
    /// </summary>
    private async Task CatchUpAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var recovered = new List<string>();
        for (var back = CatchUpDays; back >= 1; back--)
        {
            var day = today.AddDays(-back);
            IReadOnlyList<ScheduleInfo> candidates;
            try
            {
                candidates = await repository.ListCatchUpCandidatesAsync(day, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Failure(Name, $"ERRO ao listar agendamentos de {day:dd/MM}: {ex.GetType().Name}");
                continue;
            }

            foreach (var schedule in candidates)
            {
                try
                {
                    var outcome = await executor.ExecuteAsync(new ScheduleExecutionRequest(schedule.Id, day), cancellationToken);
                    if (outcome is ScheduleExecutionOutcome.Posted)
                    {
                        recovered.Add($"#{schedule.Id.ToString(CultureInfo.InvariantCulture)} ({day:dd/MM})");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.Failure(Name, $"ERRO ao recuperar Agendamento ID {schedule.Id.ToString(CultureInfo.InvariantCulture)} de {day:dd/MM}: {ex.GetType().Name}");
                }
            }
        }

        if (recovered.Count > 0)
        {
            var message = $"Recuperei {recovered.Count.ToString(CultureInfo.InvariantCulture)} lançamento(s) de dias anteriores que o motor não fez: {string.Join(", ", recovered)}";
            log.Warning(Name, message);
            alerts.Raise(Name, message);
        }
    }

    private async Task RemindAsync(ScheduleInfo schedule, DateOnly today, CancellationToken cancellationToken)
    {
        if (ScheduleRules.ReminderDueDate(schedule, today) is not { } due || string.IsNullOrEmpty(schedule.WhatsappNumber))
        {
            return;
        }

        var key = string.Create(CultureInfo.InvariantCulture, $"motor_lembrete:{schedule.Id}:{due:yyyy-MM-dd}");
        var locked = false;
        if (await cache.IsAvailableAsync(cancellationToken))
        {
            if (!await cache.SetIfNotExistsAsync(key, "sent", ReminderTtl, cancellationToken))
            {
                log.Info(Name, $"Lembrete do agendamento {schedule.Id.ToString(CultureInfo.InvariantCulture)} já enviado");
                return;
            }

            locked = true;
        }

        var sent = await bot.SendTextAsync(schedule.WhatsappNumber, ScheduleRules.ReminderMessage(schedule), cancellationToken);
        if (!sent && locked)
        {
            await cache.DeleteAsync(key, CancellationToken.None);
        }

        log.Info(Name, $"Lembrete do agendamento {schedule.Id.ToString(CultureInfo.InvariantCulture)}: {(sent ? "enviado" : "falha no envio")}");
    }

    private static string Describe(ScheduleExecutionOutcome outcome) => outcome switch
    {
        ScheduleExecutionOutcome.Posted => "transação lançada",
        ScheduleExecutionOutcome.Skipped skipped => $"pulado ({skipped.Reason})",
        _ => "sem parcela a executar ou inativo",
    };
}
