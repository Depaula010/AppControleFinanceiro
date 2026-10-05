using System.Globalization;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Api.Automation;

/// <summary>Respostas das seis rotas, com as chaves na ordem do Flask (spec 011 §3). Dinheiro como <c>double</c> (o <c>float</c> do Python).</summary>
internal static class AutomationResponses
{
    private static double Money(decimal? value) => (double)(value ?? 0);

    private static IResult Raw(Dictionary<string, object?> body, int statusCode = StatusCodes.Status200OK) => Envelopes.Json(body, statusCode);

    public static IResult Unauthorized(HttpContext _) =>
        Envelopes.B.Error(AutomationMessages.Unauthorized, StatusCodes.Status401Unauthorized);

    public static IResult BError(string message, int statusCode) => Envelopes.B.Error(message, statusCode);

    public static IResult Fail(string message) => Envelopes.B.Error(message, StatusCodes.Status500InternalServerError);

    /// <summary><c>{"status":"sucesso"}</c> sem mensagem (gravação direta do Automate).</summary>
    public static IResult AutomateSaved() => Raw(new Dictionary<string, object?> { ["status"] = "sucesso" });

    public static IResult AutomatePending(string transactionId) =>
        Envelopes.B.Success(
            AutomationMessages.AutomateSaved, new Dictionary<string, object?> { ["transaction_id"] = transactionId });

    /// <summary>Envelope do <c>/api/transacao</c>: <c>success</c>/<c>message</c> em inglês; erro <c>erro</c>/<c>mensagem</c>.</summary>
    public static IResult ApiSuccess(string message, Dictionary<string, object?>? extras = null)
    {
        var body = new Dictionary<string, object?> { ["status"] = "success", ["message"] = message };
        if (extras is not null)
        {
            foreach (var (key, value) in extras)
            {
                body[key] = value;
            }
        }

        return Raw(body);
    }

    public static IResult ApiInvalidJson() =>
        Raw(new Dictionary<string, object?> { ["erro"] = AutomationMessages.InvalidJson }, StatusCodes.Status400BadRequest);

    public static IResult SmsDatabaseUnavailable() =>
        Envelopes.E.Error(AutomationMessages.DatabaseUnavailable, StatusCodes.Status503ServiceUnavailable);

    public static IResult SmsSettled(string billDescription, int transactionId) =>
        Envelopes.B.Success(
            AutomationMessages.SmsSettled,
            new Dictionary<string, object?> { ["conta_identificada"] = billDescription, ["transaction_id"] = transactionId });

    public static IResult SmsRegistered() =>
        Envelopes.B.Success(AutomationMessages.SmsRegistered, new Dictionary<string, object?> { ["conta_identificada"] = null });

    /// <summary>O <c>id</c> do agendamento é o da URL (o <c>{id}</c> vem do endpoint).</summary>
    public static IResult ToggleUpdated(ToggleReserveResult.Updated result, int scheduleId)
    {
        return Envelopes.B.Success(
            AutomationMessages.ToggleSaved(result.Schedule.Description, result.Include),
            new Dictionary<string, object?>
            {
                ["agendamento"] = new Dictionary<string, object?>
                {
                    ["id"] = scheduleId,
                    ["descricao"] = result.Schedule.Description,
                    ["valor_previsto"] = Money(result.Schedule.ExpectedAmount),
                    ["periodicidade"] = result.Schedule.Periodicity,
                    ["tipo_agendamento"] = result.Schedule.ScheduleType,
                    ["incluir_na_reserva"] = result.Include,
                },
                ["impacto"] = new Dictionary<string, object?>
                {
                    ["gasto_mensal_anterior"] = result.Before.MonthlyEquivalent,
                    ["gasto_mensal_novo"] = result.After.MonthlyEquivalent,
                    ["reserva_ideal_anterior"] = result.Before.Total,
                    ["reserva_ideal_nova"] = result.After.Total,
                },
            });
    }

    public static IResult Listed(ListReserveResult.Listed result)
    {
        var items = result.Page.Items.Select(item => new Dictionary<string, object?>
        {
            ["id"] = item.Id,
            ["descricao"] = item.Description,
            ["valor_previsto"] = Money(item.ExpectedAmount),
            ["periodicidade"] = item.Periodicity,
            ["tipo_agendamento"] = item.ScheduleType,
            ["dia_execucao"] = item.DayOfMonth,
            ["categoria"] = item.Category,
            ["macro_categoria"] = item.MacroCategory,
            ["grupo"] = item.Group,
            ["incluir_na_reserva"] = item.IncludeInReserve,
        }).ToList();

        return Envelopes.Json(
            new Dictionary<string, object?>
            {
                ["status"] = "sucesso",
                ["total"] = result.Page.Total,
                ["limit"] = result.Filter.Limit,
                ["offset"] = result.Filter.Offset,
                ["agendamentos"] = items,
                ["resumo_reserva"] = ReserveSummary(result.Status),
            },
            StatusCodes.Status200OK);
    }

    private static Dictionary<string, object?> ReserveSummary(EmergencyReserveStatus status) => new()
    {
        ["gasto_mensal_essencial"] = status.MonthlyEquivalent,
        ["reserva_ideal"] = status.Total,
        ["meses_configurados"] = status.Months,
    };

    public static IResult Reprocessed(ReprocessInvoiceResult.Processed result)
    {
        var stats = result.Stats;
        return Envelopes.Json(
            new Dictionary<string, object?>
            {
                ["status"] = "sucesso",
                ["fatura_id"] = result.Target.InvoiceId,
                ["cartao"] = result.Target.CardName,
                ["vencimento"] = result.Target.DueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                ["status_fatura"] = result.Target.Status,
                ["stats"] = new Dictionary<string, object?>
                {
                    ["total_processados"] = stats.Processed,
                    ["fixos"] = stats.Fixed,
                    ["parcelados"] = stats.Installments,
                    ["lembretes"] = stats.Reminders,
                    ["detalhes"] = stats.Details.Select(d => new Dictionary<string, object?>
                    {
                        ["tipo"] = d.Type,
                        ["descricao"] = d.Description,
                        ["valor"] = d.Amount,
                    }).ToList(),
                },
            },
            StatusCodes.Status200OK);
    }
}
