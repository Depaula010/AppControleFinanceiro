using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Application.Jobs;

internal static class JobTimes
{
    /// <summary><c>datetime.now(TIMEZONE_BR).isoformat()</c>.</summary>
    public static string IsoNow(IClock clock)
    {
        var local = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.TimeZone);
        var text = local.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
        return text.Replace(".000000", string.Empty, StringComparison.Ordinal);
    }
}

/// <summary><c>DailyBriefingService</c> (<c>daily_briefing_service.py</c>, RN-013-22 a 24).</summary>
public sealed class DailyBriefingService(
    IBriefingDataRepository users,
    ICalendarClient calendar,
    IWeatherClient weather,
    IFinancialAlertsRepository alerts,
    ILlmClient llm,
    IJobLog log)
{
    private const string JobName = "RESUMO-MATINAL";

    /// <summary>Dados do dia; <c>null</c> se a agenda não pôde ser lida (Google desconectado ou falha).</summary>
    public async Task<BriefingData?> PrepareAsync(int userId, DateOnly date, CancellationToken cancellationToken)
    {
        IReadOnlyList<BriefingEvent> events;
        try
        {
            var raw = await calendar.GetEventsInRangeAsync(userId, date, date, cancellationToken);
            events = [.. raw.Select(e => new BriefingEvent(e.Summary, e.Start, e.End, e.Location ?? string.Empty, e.Description ?? string.Empty, e.AllDay))];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Failure(JobName, $"Erro ao buscar eventos do usuário {userId.ToString(CultureInfo.InvariantCulture)}: {ex.GetType().Name}");
            return null;
        }

        var location = await users.GetLocationAsync(userId, cancellationToken);
        BriefingWeather? main = null;
        if (!string.IsNullOrEmpty(location.City))
        {
            main = await WeatherAsync(location.City, location.State, userId, cancellationToken);
        }

        var extra = new List<BriefingExtraWeather>();
        foreach (var (city, state) in BriefingRules.ExtractLocations(events))
        {
            if (city == location.City && state == location.State)
            {
                continue;
            }

            if (await WeatherAsync(city, state, userId, cancellationToken) is { } found)
            {
                extra.Add(new BriefingExtraWeather(city, state, found));
            }
        }

        var remote = 0;
        var inPerson = 0;
        foreach (var evt in events)
        {
            switch (BriefingRules.DetectType(evt))
            {
                case "remoto":
                    remote++;
                    break;
                case "presencial":
                    inPerson++;
                    break;
            }
        }

        FinancialAlerts financial;
        try
        {
            financial = await alerts.GetUpcomingAsync(userId, date, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Failure(JobName, $"Erro ao buscar alertas financeiros: {ex.GetType().Name}");
            financial = FinancialAlerts.Empty;
        }

        return new BriefingData(events, main, extra, BriefingRules.CalculateGaps(events), remote, inPerson, financial, date);
    }

    /// <summary>Texto final: sem eventos, a mensagem simples; com eventos, a IA (com fallback básico).</summary>
    public async Task<string> GenerateMessageAsync(BriefingData data, int userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.TotalEvents == 0)
        {
            return BriefingRules.NoEventsMessage(data);
        }

        try
        {
            // Desvio 138: a chave e o uso são do usuário (o Python usava a chave do sistema).
            var text = await llm.GenerateTextAsync(new LlmRequest(BriefingPrompt.Build(data), userId), cancellationToken);
            return BriefingPrompt.CleanResponse(text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Failure(JobName, $"Erro ao gerar resumo: {ex.GetType().Name}");
            return BriefingPrompt.Fallback(data);
        }
    }

    private async Task<BriefingWeather?> WeatherAsync(string city, string? state, int userId, CancellationToken cancellationToken)
    {
        // Desvio 138: a chave e o uso são do usuário (o Python usava a chave do sistema).
        var info = await weather.GetWeatherAsync(city, state, userId, cancellationToken);
        return info is null ? null : new BriefingWeather(info.FullDescription, info.Temperature, info.FeelsLike, info.RainChance);
    }
}

/// <summary>Resumo matinal de hora em hora (<c>daily_briefing.py</c>) e as portas das rotas <c>trigger-daily-briefing</c> e <c>test-daily-briefing</c>.</summary>
public sealed class DailyBriefingJob(
    IJobNotificationConfigRepository config,
    DailyBriefingService briefing,
    IFinancialAlertsRepository alerts,
    IBotClient bot,
    IBriefingDataRepository users,
    IClock clock,
    IJobLog log) : IScheduledJob, IDailyBriefingJob
{
    public string Name => JobSchedules.MorningSummary;

    public string LockName => JobSchedules.MorningSummary;

    /// <summary><c>montar_mensagem_unificada</c> (RN-013-21).</summary>
    public static string? Unify(bool summaryActive, bool alertsActive, string? summary, string? alertsText)
    {
        if (summaryActive && alertsActive)
        {
            return !string.IsNullOrEmpty(summary) ? summary : !string.IsNullOrEmpty(alertsText) ? alertsText : null;
        }

        if (summaryActive && !string.IsNullOrEmpty(summary))
        {
            return summary;
        }

        if (alertsActive && !summaryActive && !string.IsNullOrEmpty(alertsText))
        {
            return alertsText;
        }

        return null;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        var hour = JobClock.HourMinute(now);
        log.Info(Name, $"Buscando usuários para notificar às {JobClock.Hhmm(hour)}");
        var recipients = await config.ListMorningUsersAsync(hour, cancellationToken);
        if (recipients.Count == 0)
        {
            log.Info(Name, $"Nenhum usuário configurado para este horário ({JobClock.Hhmm(hour)})");
            return;
        }

        log.Info(Name, $"{recipients.Count.ToString(CultureInfo.InvariantCulture)} usuário(s) encontrado(s)");
        var today = clock.Today;
        foreach (var recipient in recipients)
        {
            var id = recipient.UserId.ToString(CultureInfo.InvariantCulture);
            try
            {
                var settings = await config.GetOrCreateAsync(recipient.UserId, cancellationToken);
                string? summary = null;
                string? alertsText = null;

                if (settings.MorningSummaryActive)
                {
                    try
                    {
                        var data = await briefing.PrepareAsync(recipient.UserId, today, cancellationToken);
                        if (data is null)
                        {
                            log.Failure(Name, $"Erro ao preparar dados para usuário {id}");
                        }
                        else
                        {
                            summary = await briefing.GenerateMessageAsync(data, recipient.UserId, cancellationToken);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Desvio 137: a falha do resumo não pode levar embora os alertas financeiros.
                        log.Failure(Name, $"Erro ao gerar o resumo do usuário {id}: {ex.GetType().Name}");
                    }
                }

                // Desvio 137: com o resumo ativo mas sem texto (ex.: Google desconectado), os alertas saem mesmo assim.
                if (settings.FinancialAlertsActive && (!settings.MorningSummaryActive || string.IsNullOrEmpty(summary)))
                {
                    var upcoming = await alerts.GetUpcomingAsync(recipient.UserId, today, cancellationToken);
                    if (upcoming.Any)
                    {
                        alertsText = FinancialAlertFormatter.Format(upcoming, includeGreeting: true);
                    }
                    else
                    {
                        log.Info(Name, $"Sem alertas financeiros para usuário {id}");
                    }
                }

                var message = Unify(settings.MorningSummaryActive, settings.FinancialAlertsActive, summary, alertsText);
                if (message is null)
                {
                    log.Info(Name, $"Nenhuma mensagem para enviar ao usuário {id}");
                    continue;
                }

                // Desvio 139: o retorno do bot é conferido; falha vira erro registrado, não "enviada".
                if (await bot.SendTextAsync(recipient.WhatsappNumber, message, cancellationToken))
                {
                    log.Info(Name, $"Mensagem enviada para usuário {id}");
                }
                else
                {
                    log.Failure(Name, $"Falha ao enviar a mensagem para usuário {id}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Failure(Name, $"Erro ao processar usuário {id}: {ex.GetType().Name}");
            }
        }

        log.Info(Name, "Processamento finalizado");
    }

    public async Task<DailyBriefingRunResult> RunForCurrentMinuteAsync(CancellationToken cancellationToken)
    {
        var hour = JobClock.HourMinute(clock.Now);
        var recipients = await config.ListMorningUsersAsync(hour, cancellationToken);
        if (recipients.Count == 0)
        {
            return new DailyBriefingRunResult(0, 0, 0, JobClock.Hhmm(hour), NoUsers: true);
        }

        var today = clock.Today;
        var sent = 0;
        var errors = 0;
        foreach (var recipient in recipients)
        {
            try
            {
                var data = await briefing.PrepareAsync(recipient.UserId, today, cancellationToken);
                if (data is null)
                {
                    errors++;
                    continue;
                }

                var message = await briefing.GenerateMessageAsync(data, recipient.UserId, cancellationToken);
                if (string.IsNullOrEmpty(message))
                {
                    errors++;
                    continue;
                }

                if (await bot.SendTextAsync(recipient.WhatsappNumber, message, cancellationToken))
                {
                    sent++;
                }
                else
                {
                    errors++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors++;
                log.Failure(Name, $"Erro ao processar usuário {recipient.UserId.ToString(CultureInfo.InvariantCulture)}: {ex.GetType().Name}");
            }
        }

        return new DailyBriefingRunResult(recipients.Count, sent, errors, JobClock.Hhmm(hour), NoUsers: false);
    }

    public async Task<DailyBriefingTestResult> SendTestAsync(int userId, CancellationToken cancellationToken)
    {
        var data = await briefing.PrepareAsync(userId, clock.Today, cancellationToken);
        if (data is null)
        {
            return new DailyBriefingTestResult(DailyBriefingTestOutcome.PrepareFailed, 0, null);
        }

        var message = await briefing.GenerateMessageAsync(data, userId, cancellationToken);
        if (string.IsNullOrEmpty(message))
        {
            return new DailyBriefingTestResult(DailyBriefingTestOutcome.EmptyMessage, data.TotalEvents, null);
        }

        var user = await users.GetLocationAsync(userId, cancellationToken);
        var sent = !string.IsNullOrEmpty(user.WhatsappNumber) && await bot.SendTextAsync(user.WhatsappNumber, message, cancellationToken);
        return new DailyBriefingTestResult(
            sent ? DailyBriefingTestOutcome.Sent : DailyBriefingTestOutcome.SendFailed, data.TotalEvents, message);
    }
}

/// <summary><c>processar_contas_vencer</c> (<c>notification_processor_service.py</c>, RN-013-33); só pela rota admin.</summary>
public sealed class BillsNotificationJob(
    IJobNotificationConfigRepository config,
    IBillsReminderRepository bills,
    IBotClient bot,
    IClock clock,
    IJobLog log) : IBillsNotificationJob
{
    private const string JobName = "BILLS-NOTIF";

    public async Task<BillsJobResult> RunAsync(CancellationToken cancellationToken)
    {
        var (min, max) = JobClock.Window(JobClock.HourMinute(clock.Now), 5);
        var recipients = await config.ListBillsReminderUsersAsync(min, max, cancellationToken);
        var today = clock.Today;
        var target = today.AddDays(1);
        var sent = 0;
        var errors = 0;

        foreach (var recipient in recipients)
        {
            try
            {
                var list = await bills.ListAsync(recipient.UserId, target, today, cancellationToken);
                var message = BillsReminderFormatter.Format(recipient.Name, list, target);
                if (message is null)
                {
                    continue;
                }

                if (await bot.SendTextAsync(recipient.WhatsappNumber, message, cancellationToken))
                {
                    sent++;
                }
                else
                {
                    errors++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors++;
                log.Failure(JobName, $"Erro ao processar usuário {recipient.UserId.ToString(CultureInfo.InvariantCulture)}: {ex.GetType().Name}");
            }
        }

        return new BillsJobResult(recipients.Count, sent, errors, JobTimes.IsoNow(clock));
    }
}

/// <summary><c>NightlyCheckinService</c>: snapshot, sessão no Redis e mensagem (RN-013-42 a 45). Consumido também pela 012.</summary>
public sealed class NightlyCheckinService(ICheckinQueries queries, ICache cache, IClock clock)
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(1);

    public Task<CheckinSnapshot> CollectSnapshotAsync(int userId, DateOnly date, CancellationToken cancellationToken) =>
        queries.GetSnapshotAsync(userId, date, cancellationToken);

    /// <summary><c>checkin_id</c>: 8 caracteres de um UUID.</summary>
    public static string NewCheckinId() => Guid.NewGuid().ToString()[..8];

    /// <summary>Grava a sessão e a flag ativa (1 h); devolve o <c>checkin_id</c> (<paramref name="checkinId"/> ou um novo).</summary>
    public async Task<string> CreateSessionAsync(
        string whatsappNumber, IEnumerable<CheckinBill> bills, CancellationToken cancellationToken, string? checkinId = null)
    {
        checkinId ??= NewCheckinId();
        var session = CheckinSession.Build(bills, clock.Today);
        await cache.SetJsonAsync(CacheKeys.NightlyCheckin(whatsappNumber, checkinId), session, SessionTtl, cancellationToken);
        await cache.SetJsonAsync(CacheKeys.NightlyCheckinActive(whatsappNumber), checkinId, SessionTtl, cancellationToken);
        return checkinId;
    }

    /// <summary>Apaga a sessão e a flag ativa (desvio 139: o envio falhou e o usuário não viu o check-in).</summary>
    public async Task DeleteSessionAsync(string whatsappNumber, string checkinId, CancellationToken cancellationToken)
    {
        await cache.DeleteAsync(CacheKeys.NightlyCheckin(whatsappNumber, checkinId), cancellationToken);
        await cache.DeleteAsync(CacheKeys.NightlyCheckinActive(whatsappNumber), cancellationToken);
    }

    public string? FormatConsolidatedMessage(CheckinSnapshot snapshot, string? checkinId) =>
        CheckinMessageFormatter.Format(snapshot, clock.Today, checkinId);
}

/// <summary>Check-in noturno, 18 a 23 h (<c>nightly_checkin.py</c>, spec 013 §4.5).</summary>
public sealed class NightlyCheckinJob(
    IJobNotificationConfigRepository config,
    NightlyCheckinService checkin,
    IBotClient bot,
    ICache cache,
    IClock clock,
    IJobLog log) : IScheduledJob
{
    public string Name => JobSchedules.NightlyCheckin;

    public string LockName => JobSchedules.NightlyCheckin;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.Now;
        if (now.Hour is < 18 or > 23)
        {
            log.Warning(Name, $"Job executado fora da janela permitida (18h-23h). Hora atual: {now.Hour.ToString(CultureInfo.InvariantCulture)}h");
        }

        if (!await cache.IsAvailableAsync(cancellationToken))
        {
            log.Failure(Name, "Redis indisponível - abortando");
            throw new InvalidOperationException("Redis indisponível");
        }

        var hour = JobClock.HourMinute(now);
        log.Info(Name, $"Buscando usuários para {JobClock.Hhmm(hour)}");
        var recipients = await config.ListCheckinUsersAsync(hour, cancellationToken);
        if (recipients.Count == 0)
        {
            log.Info(Name, $"Nenhum usuário configurado para {JobClock.Hhmm(hour)}");
            return;
        }

        log.Info(Name, $"{recipients.Count.ToString(CultureInfo.InvariantCulture)} usuário(s) encontrado(s)");
        var today = clock.Today;
        foreach (var recipient in recipients)
        {
            var id = recipient.UserId.ToString(CultureInfo.InvariantCulture);
            try
            {
                var snapshot = await checkin.CollectSnapshotAsync(recipient.UserId, today, cancellationToken);
                if (snapshot.IsEmpty)
                {
                    log.Info(Name, $"Nenhuma pendência para usuário {id}");
                    continue;
                }

                // Desvio 130: a mensagem é montada antes de a sessão existir (fatura vencida já não derruba o texto).
                var checkinId = NightlyCheckinService.NewCheckinId();
                var message = checkin.FormatConsolidatedMessage(snapshot, checkinId);
                if (string.IsNullOrEmpty(message))
                {
                    log.Info(Name, $"Sem conteúdo para enviar - usuário {id}");
                    continue;
                }

                await checkin.CreateSessionAsync(
                    recipient.WhatsappNumber, [.. snapshot.PendingBills, .. snapshot.OverdueBills], cancellationToken, checkinId);

                // Desvio 139: a sessão só vale se o usuário recebeu o check-in.
                var sent = false;
                try
                {
                    sent = await bot.SendTextAsync(recipient.WhatsappNumber, message, cancellationToken);
                }
                finally
                {
                    if (!sent)
                    {
                        await checkin.DeleteSessionAsync(recipient.WhatsappNumber, checkinId, CancellationToken.None);
                    }
                }

                if (!sent)
                {
                    log.Failure(Name, $"Falha ao enviar o check-in para usuário {id}; sessão removida");
                    continue;
                }

                log.Info(Name, $"Check-in consolidado enviado para usuário {id}: {snapshot.PendingBills.Count.ToString(CultureInfo.InvariantCulture)} pendente(s), " +
                    $"{snapshot.OverdueBills.Count.ToString(CultureInfo.InvariantCulture)} atrasada(s), {snapshot.OverdueInvoices.Count.ToString(CultureInfo.InvariantCulture)} fatura(s) vencida(s)");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.Failure(Name, $"Erro ao processar usuário {id}: {ex.GetType().Name}");
            }
        }

        log.Info(Name, "Processamento finalizado");
    }
}

/// <summary>Relatórios mensais (<c>monthly_report_processor_service.py</c>, RN-013-70 a 76).</summary>
public sealed class MonthlyReportJob(
    IMonthlyReportRepository repository,
    IBotClient bot,
    IChartRenderer chart,
    IClock clock,
    IJobLog log) : IMonthlyReportJob
{
    private const string JobName = "RELATORIO-MENSAL";
    private const string TextFailed = "Falha ao enviar mensagem de texto";

    public async Task<MonthlyReportRunResult> RunAsync(MonthlyMoment moment, CancellationToken cancellationToken)
    {
        var start = clock.UtcNow;
        var local = TimeZoneInfo.ConvertTime(start, clock.TimeZone);
        var users = new List<MonthlyReportUserEntry>();
        var errors = new List<MonthlyReportErrorEntry>();
        var total = 0;
        var sent = 0;
        var failed = 0;

        try
        {
            var (min, max) = JobClock.Window(TimeOnly.FromDateTime(clock.Now), 5);
            var recipients = await repository.ListRecipientsAsync(moment, min, max, cancellationToken);
            total = recipients.Count;
            foreach (var recipient in recipients)
            {
                var hour = recipient.SendTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                try
                {
                    var data = await repository.GetDataAsync(recipient.UserId, moment, clock.Today, cancellationToken);
                    if (data.Totals.TotalTransactions == 0)
                    {
                        users.Add(new MonthlyReportUserEntry(recipient.UserId, recipient.Name, "sem_dados", "Nenhuma transação no período", null, null));
                        continue;
                    }

                    var (textSent, chartSent) = await DeliverAsync(recipient, data, cancellationToken);
                    if (!textSent)
                    {
                        throw new InvalidOperationException(TextFailed);
                    }

                    sent++;
                    users.Add(new MonthlyReportUserEntry(recipient.UserId, recipient.Name, "enviado", "Relatório enviado com sucesso", hour, chartSent));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failed++;
                    var reason = ex is InvalidOperationException io && io.Message == TextFailed ? TextFailed : "Erro ao processar o relatório";
                    log.Failure(JobName, $"Erro ao processar usuário {recipient.UserId.ToString(CultureInfo.InvariantCulture)}: {ex.GetType().Name}");
                    errors.Add(new MonthlyReportErrorEntry(recipient.UserId, recipient.Name, null, reason));
                    users.Add(new MonthlyReportUserEntry(recipient.UserId, recipient.Name, "erro", reason, null, null));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Failure(JobName, $"Erro geral no processamento: {ex.GetType().Name}");
            errors.Add(new MonthlyReportErrorEntry(null, null, "erro_geral", "Erro geral no processamento"));
        }

        var seconds = (clock.UtcNow - start).TotalSeconds;
        return new MonthlyReportRunResult(
            MonthlyReportRules.MomentName(moment), local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            total, sent, failed, users, errors, seconds);
    }

    public async Task<ManualReportResult> SendManualAsync(int userId, MonthlyMoment moment, CancellationToken cancellationToken)
    {
        try
        {
            var user = await repository.FindUserAsync(userId, cancellationToken);
            if (user is null)
            {
                return new ManualReportResult(false, "Usuário não encontrado", userId, null, false, false, null);
            }

            var data = await repository.GetDataAsync(userId, moment, clock.Today, cancellationToken);
            if (data.Totals.TotalTransactions == 0)
            {
                return new ManualReportResult(false, "Nenhuma transação encontrada no período", userId, user.Name, false, false, data);
            }

            var (textSent, chartSent) = await DeliverAsync(user, data, cancellationToken);
            return textSent
                ? new ManualReportResult(true, null, userId, user.Name, true, chartSent, data)
                : new ManualReportResult(false, TextFailed, userId, user.Name, false, false, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Failure(JobName, $"Erro no envio manual: {ex.GetType().Name}");
            return new ManualReportResult(false, "Erro ao enviar o relatório mensal", userId, null, false, false, null);
        }
    }

    /// <summary>Texto, depois gráfico; falha do gráfico só registra (RN-013-74).</summary>
    private async Task<(bool Text, bool Chart)> DeliverAsync(ReportRecipient recipient, MonthlyReportData data, CancellationToken cancellationToken)
    {
        var message = MonthlyReportFormatter.Format(data, recipient.Name);
        var days = data.PeriodEnd.DayNumber - data.PeriodStart.DayNumber + 1;
        var image = chart.RenderPie([.. data.TopCategories.Select(c => (c.Category, Math.Abs(c.Value)))], days);

        if (!await bot.SendTextAsync(recipient.WhatsappNumber, message, cancellationToken))
        {
            return (false, false);
        }

        var chartSent = image is not null
            && await bot.SendImageAsync(recipient.WhatsappNumber, image, MonthlyReportRules.Caption(data), cancellationToken);
        if (!chartSent)
        {
            log.Warning(JobName, $"Falha ao enviar gráfico para usuário {recipient.UserId.ToString(CultureInfo.InvariantCulture)}");
        }

        return (true, chartSent);
    }
}

/// <summary>Relatório do mês anterior: dia 1 às 08:00 (job 2).</summary>
public sealed class StartOfMonthReportJob(MonthlyReportJob report, IJobLog log) : IScheduledJob
{
    public string Name => JobSchedules.StartOfMonthReport;

    public string LockName => JobSchedules.StartOfMonthReport;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var result = await report.RunAsync(MonthlyMoment.InicioMes, cancellationToken);
        log.Info(Name, $"Processamento concluído: {result.Sent.ToString(CultureInfo.InvariantCulture)} enviados, {result.Failed.ToString(CultureInfo.InvariantCulture)} erros");
    }
}

/// <summary>Relatório do mês corrente: todo dia às 23:00, só no último dia do mês (job 3; desvio 020).</summary>
public sealed class EndOfMonthReportJob(MonthlyReportJob report, IClock clock, IJobLog log) : IScheduledJob
{
    public string Name => JobSchedules.EndOfMonthReport;

    public string LockName => JobSchedules.EndOfMonthReport;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!JobClock.IsLastDayOfMonth(clock.Today))
        {
            log.Info(Name, "Hoje não é o último dia do mês: nada a enviar");
            return;
        }

        var result = await report.RunAsync(MonthlyMoment.FimMes, cancellationToken);
        log.Info(Name, $"Processamento concluído: {result.Sent.ToString(CultureInfo.InvariantCulture)} enviados, {result.Failed.ToString(CultureInfo.InvariantCulture)} erros");
    }
}
