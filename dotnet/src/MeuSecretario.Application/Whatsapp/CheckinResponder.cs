using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Diagnóstico sem PII da resposta ao check-in (a Application não conhece <c>ILogger</c>; a Api implementa): só o tipo da exceção.</summary>
public interface ICheckinDiagnostics
{
    void ReplyFailed(string errorType);

    /// <summary>Item que a resposta pediu e que não virou lançamento: só o id do agendamento e o motivo.</summary>
    void ItemNotConfirmed(int scheduleId, string reason)
    {
    }
}

/// <summary>
/// Resposta do usuário ao check-in noturno (<c>NightlyCheckinService.process_response</c> e <c>mark_bills_as_paid</c>,
/// <c>nightly_checkin_service.py:482-874</c>; AC-012-404). Lê a sessão que o job da 013 gravou, interpreta a resposta e lança cada
/// conta confirmada pela rotina única do motor de agendamentos (<see cref="IScheduleExecutor"/>, desvio 011: grava o
/// <c>agendamento_id</c> e não duplica o que já foi lançado no mês). A sessão e a flag ativa somem sempre, inclusive em exceção.
/// </summary>
public sealed class CheckinResponder(
    ICache cache, ICheckinSession session, IScheduleExecutor executor, IClock clock, ICheckinDiagnostics diagnostics) : INightlyCheckinResponder
{
    public async Task<string?> RespondAsync(string number, string message, string checkinId, CancellationToken cancellationToken)
    {
        var sessionKey = CacheKeys.NightlyCheckin(number, checkinId);
        var data = await cache.GetJsonAsync<CheckinSession>(sessionKey, cancellationToken);
        if (data is null)
        {
            await session.ClearActiveAsync(number, cancellationToken);
            return CheckinReplyTexts.Expired;
        }

        if (data.TotalItems == 0)
        {
            await EndAsync(number, sessionKey, cancellationToken);
            return CheckinReplyTexts.NoRecentItems;
        }

        var reply = CheckinReplyParser.Parse(message, data.TotalItems);
        switch (reply.Kind)
        {
            case CheckinReplyKind.Defer:
                await EndAsync(number, sessionKey, cancellationToken);
                return CheckinReplyTexts.Deferred;
            case CheckinReplyKind.Error:
            case CheckinReplyKind.Invalid:
                await EndAsync(number, sessionKey, cancellationToken);
                return reply.Text;
        }

        var items = reply.Indices
            .Select(index => data.Items.GetValueOrDefault(index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .OfType<CheckinSessionItem>()
            .ToList();
        if (items.Count == 0)
        {
            return CheckinReplyTexts.NoValidItems;
        }

        try
        {
            var (confirmed, notConfirmed) = await PostAsync(items, cancellationToken);
            await EndAsync(number, sessionKey, cancellationToken);
            if (confirmed.Count == 0)
            {
                return notConfirmed.Count == 0
                    ? CheckinReplyTexts.NoValidItems
                    : CheckinReplyTexts.NotConfirmed(notConfirmed, includeHeader: false);
            }

            var text = CheckinReplyTexts.Confirmed(confirmed, data.TotalItems);
            return notConfirmed.Count == 0 ? text : $"{text}\n\n{CheckinReplyTexts.NotConfirmed(notConfirmed, includeHeader: true)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.ReplyFailed(ex.GetType().Name);
            await EndAsync(number, sessionKey, cancellationToken);
            return CheckinReplyTexts.SaveFailed;
        }
    }

    private async Task<(List<string> Confirmed, List<(string Description, string Reason)> NotConfirmed)> PostAsync(
        List<CheckinSessionItem> items, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var confirmed = new List<string>();
        var notConfirmed = new List<(string Description, string Reason)>();
        foreach (var item in items)
        {
            var date = CheckinReplyTexts.PaymentDate(item.DueDate, item.Day, today);
            var outcome = await executor.ExecuteAsync(new ScheduleExecutionRequest(item.Id, date), cancellationToken);

            // Já lançada no mês (Skipped) conta como confirmada: a conta está paga, só não se duplica o lançamento.
            if (outcome is ScheduleExecutionOutcome.Posted or ScheduleExecutionOutcome.Skipped)
            {
                confirmed.Add(item.Description);
            }
            else
            {
                diagnostics.ItemNotConfirmed(item.Id, outcome.GetType().Name);
                notConfirmed.Add((item.Description, CheckinReplyTexts.NotEligibleReason));
            }
        }

        return (confirmed, notConfirmed);
    }

    private async Task EndAsync(string number, string sessionKey, CancellationToken cancellationToken)
    {
        await cache.DeleteAsync(sessionKey, cancellationToken);
        await session.ClearActiveAsync(number, cancellationToken);
    }
}
