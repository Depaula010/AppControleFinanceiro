using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Automation;
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
/// <c>nightly_checkin_service.py</c>; AC-012-404). Lê a sessão que o job da 013 gravou, interpreta a resposta e lança cada
/// conta confirmada pela rotina única do motor de agendamentos (<see cref="IScheduleExecutor"/>, desvio 011: grava o
/// <c>agendamento_id</c> e não duplica o que já foi lançado no mês). A sessão e a flag ativa somem sempre, inclusive em exceção.
/// <para>
/// Desvio 252: <c>LEMBRETE_VARIAVEL</c> (conta de valor que muda todo mês) não é lançado pelo valor previsto às cegas: depois do "ok" o bot
/// pergunta o valor de cada um (um número, "ok" para o previsto ou "pular"), na mesma janela de 1 h do check-in.
/// </para>
/// </summary>
public sealed class CheckinResponder(
    CheckinDialogStore dialog, IScheduleExecutor executor, CheckinInvoicePayer invoicePayer, IClock clock, ICheckinDiagnostics diagnostics)
    : INightlyCheckinResponder
{
    public async Task<string?> RespondAsync(string number, string message, string checkinId, CancellationToken cancellationToken)
    {
        switch (await dialog.LoadAsync(number, checkinId, cancellationToken))
        {
            case CheckinDialogState.AwaitingValues awaiting when awaiting.Pending.Items.Count > 0:
                return await RespondValueAsync(number, message, checkinId, awaiting.Pending, cancellationToken);
            case CheckinDialogState.AwaitingValues awaiting:
                return await RespondInvoiceAsync(number, message, checkinId, awaiting.Pending, cancellationToken);
            case CheckinDialogState.AwaitingConfirmation confirmation:
                return await RespondConfirmationAsync(number, message, checkinId, confirmation.Session, cancellationToken);
            default:
                await dialog.ExpireAsync(number, cancellationToken);
                return CheckinReplyTexts.Expired;
        }
    }

    /// <summary>A resposta ao check-in em si: "ok", números ou "depois".</summary>
    private async Task<string?> RespondConfirmationAsync(
        string number, string message, string checkinId, CheckinSession data, CancellationToken cancellationToken)
    {
        if (data.TotalItems == 0)
        {
            await dialog.EndAsync(number, checkinId, cancellationToken);
            return CheckinReplyTexts.NoRecentItems;
        }

        var reply = CheckinReplyParser.Parse(message, data.TotalItems);
        switch (reply.Kind)
        {
            case CheckinReplyKind.Defer:
                await dialog.EndAsync(number, checkinId, cancellationToken);
                return CheckinReplyTexts.Deferred;
            case CheckinReplyKind.Error:
            case CheckinReplyKind.Invalid:
                await dialog.EndAsync(number, checkinId, cancellationToken);
                return reply.Text;
        }

        var items = reply.Indices
            .Select(index => data.Items.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture)))
            .OfType<CheckinSessionItem>()
            .ToList();
        var chosenInvoices = reply.Indices
            .Select(index => data.Invoices.GetValueOrDefault(index.ToString(CultureInfo.InvariantCulture)))
            .OfType<CheckinInvoiceItem>()
            .ToList();
        if (items.Count == 0 && chosenInvoices.Count == 0)
        {
            return CheckinReplyTexts.NoValidItems;
        }

        var direct = items.Where(item => item.Kind != ScheduleKinds.VariableReminder).ToList();
        var variable = items.Where(item => item.Kind == ScheduleKinds.VariableReminder).ToList();

        try
        {
            var (confirmed, notConfirmed) = await PostAsync(direct, cancellationToken);

            if (variable.Count > 0 || chosenInvoices.Count > 0)
            {
                // A sessão das contas some; a flag ativa e as perguntas (valores, depois contas das faturas) ficam até a última ser respondida.
                var pending = new CheckinPendingValues(variable, variable.Count, chosenInvoices, chosenInvoices.Count);
                await dialog.StartValuesAsync(number, checkinId, pending, cancellationToken);
                return Join(Summary(confirmed, notConfirmed, data.Items.Count), await NextQuestionAsync(pending, cancellationToken));
            }

            await dialog.EndAsync(number, checkinId, cancellationToken);
            return confirmed.Count == 0 && notConfirmed.Count == 0
                ? CheckinReplyTexts.NoValidItems
                : Summary(confirmed, notConfirmed, data.Items.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.ReplyFailed(ex.GetType().Name);
            await dialog.EndAsync(number, checkinId, cancellationToken);
            return CheckinReplyTexts.SaveFailed;
        }
    }

    /// <summary>Uma resposta à pergunta do valor do primeiro lembrete da fila.</summary>
    private async Task<string> RespondValueAsync(
        string number, string message, string checkinId, CheckinPendingValues queue, CancellationToken cancellationToken)
    {
        var item = queue.Items[0];
        var reply = CheckinValueParser.Parse(message);

        if (reply.Kind == CheckinValueKind.Invalid)
        {
            await dialog.SaveValuesAsync(number, checkinId, queue, cancellationToken);
            return Join(CheckinReplyTexts.ValueInvalid, Question(queue));
        }

        string line;
        if (reply.Kind == CheckinValueKind.Skip)
        {
            line = CheckinReplyTexts.ValueSkipped(item.Description);
        }
        else
        {
            decimal? amount = reply.Kind == CheckinValueKind.Amount ? reply.Amount : Expected(item);
            if (amount is not > 0)
            {
                await dialog.SaveValuesAsync(number, checkinId, queue, cancellationToken);
                return Join(CheckinReplyTexts.ValueNeeded, Question(queue));
            }

            try
            {
                var date = CheckinReplyTexts.PaymentDate(item.DueDate, item.Day, clock.Today);
                var outcome = await executor.ExecuteAsync(
                    new ScheduleExecutionRequest(item.Id, date, AllowVariableReminder: true, Amount: amount, Competence: Competence(item)), cancellationToken);
                switch (outcome)
                {
                    case ScheduleExecutionOutcome.Posted:
                        line = CheckinReplyTexts.ValuePosted(item.Description, amount.Value);
                        break;
                    case ScheduleExecutionOutcome.Skipped:
                        line = CheckinReplyTexts.ValueAlreadyPosted(item.Description);
                        break;
                    default:
                        diagnostics.ItemNotConfirmed(item.Id, outcome.GetType().Name);
                        line = CheckinReplyTexts.NotConfirmed([(item.Description, CheckinReplyTexts.NotEligibleReason)], includeHeader: true);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                diagnostics.ReplyFailed(ex.GetType().Name);
                await dialog.EndAsync(number, checkinId, cancellationToken);
                return CheckinReplyTexts.SaveFailed;
            }
        }

        var rest = queue with { Items = [.. queue.Items.Skip(1)] };
        if (rest.Items.Count == 0 && !rest.HasInvoices)
        {
            await dialog.EndAsync(number, checkinId, cancellationToken);
            return line;
        }

        await dialog.SaveValuesAsync(number, checkinId, rest, cancellationToken);
        return Join(line, await NextQuestionAsync(rest, cancellationToken));
    }

    /// <summary>Uma resposta à pergunta "pagar a fatura com qual conta?" (desvio 264).</summary>
    private async Task<string> RespondInvoiceAsync(
        string number, string message, string checkinId, CheckinPendingValues queue, CancellationToken cancellationToken)
    {
        var invoice = queue.Invoices![0];
        string line;
        try
        {
            var accounts = await invoicePayer.PayingAccountsAsync(invoice.UserId, cancellationToken);
            var reply = CheckinInvoiceReplyParser.Parse(message, accounts.Count);
            if (reply.Kind == CheckinInvoiceReplyKind.Invalid)
            {
                await dialog.SaveValuesAsync(number, checkinId, queue, cancellationToken);
                return Join(CheckinReplyTexts.InvoiceInvalid, InvoiceQuestion(queue, accounts));
            }

            if (reply.Kind == CheckinInvoiceReplyKind.Skip)
            {
                line = CheckinReplyTexts.InvoiceSkipped(invoice.Card);
            }
            else
            {
                var outcome = await invoicePayer.PayAsync(invoice, accounts[reply.Index - 1], cancellationToken);
                line = outcome is CheckinInvoiceOutcome.Paid paid
                    ? CheckinReplyTexts.InvoicePaid(invoice.Card, paid.Amount, paid.AccountName)
                    : CheckinReplyTexts.InvoiceFailed(invoice.Card);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.ReplyFailed(ex.GetType().Name);
            await dialog.EndAsync(number, checkinId, cancellationToken);
            return CheckinReplyTexts.SaveFailed;
        }

        var rest = queue with { Invoices = [.. queue.Invoices.Skip(1)] };
        if (!rest.HasInvoices)
        {
            await dialog.EndAsync(number, checkinId, cancellationToken);
            return line;
        }

        await dialog.SaveValuesAsync(number, checkinId, rest, cancellationToken);
        return Join(line, await NextQuestionAsync(rest, cancellationToken));
    }

    /// <summary>A próxima pergunta da fila: primeiro os valores dos lembretes, depois a conta de cada fatura.</summary>
    private async Task<string> NextQuestionAsync(CheckinPendingValues queue, CancellationToken cancellationToken) =>
        queue.Items.Count > 0
            ? Question(queue)
            : InvoiceQuestion(queue, await invoicePayer.PayingAccountsAsync(queue.Invoices![0].UserId, cancellationToken));

    private static string InvoiceQuestion(CheckinPendingValues queue, IReadOnlyList<AccountRef> accounts)
    {
        var invoice = queue.Invoices![0];
        return CheckinReplyTexts.InvoiceQuestion(
            invoice.Card, (decimal)invoice.Amount, [.. accounts.Select(a => a.Name)], queue.InvoiceTotal - queue.Invoices.Count + 1, queue.InvoiceTotal);
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
            var outcome = await executor.ExecuteAsync(new ScheduleExecutionRequest(item.Id, date, AllowVariableReminder: true, Competence: Competence(item)), cancellationToken);

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

    /// <summary>Texto das contas lançadas na hora e das que não deu para lançar; vazio se não houve nenhuma.</summary>
    private static string Summary(List<string> confirmed, List<(string Description, string Reason)> notConfirmed, int totalItems)
    {
        if (confirmed.Count == 0)
        {
            return notConfirmed.Count == 0 ? string.Empty : CheckinReplyTexts.NotConfirmed(notConfirmed, includeHeader: false);
        }

        var text = CheckinReplyTexts.Confirmed(confirmed, totalItems);
        return notConfirmed.Count == 0 ? text : Join(text, CheckinReplyTexts.NotConfirmed(notConfirmed, includeHeader: true));
    }

    private static string Join(string first, string second) => first.Length == 0 ? second : $"{first}\n\n{second}";

    /// <summary>Vencimento que a confirmação quita (<c>data_vencimento_real</c> da sessão); sem ele, a data do lançamento.</summary>
    private static DateOnly? Competence(CheckinSessionItem item) =>
        DateOnly.TryParseExact(item.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due) ? due : null;

    private static decimal? Expected(CheckinSessionItem item) => item.ExpectedAmount is { } value ? (decimal)value : null;

    private static string Question(CheckinPendingValues queue)
    {
        var item = queue.Items[0];
        return CheckinReplyTexts.ValueQuestion(item.Description, Expected(item), queue.Total - queue.Items.Count + 1, queue.Total);
    }
}
