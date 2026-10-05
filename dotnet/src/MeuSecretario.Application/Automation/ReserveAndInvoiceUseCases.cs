using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Application.Automation;

public abstract record ToggleReserveResult
{
    public sealed record MissingFields : ToggleReserveResult;

    public sealed record Unauthorized : ToggleReserveResult;

    public sealed record NotFound : ToggleReserveResult;

    public sealed record Updated(
        ReserveScheduleRow Schedule, bool Include, EmergencyReserveStatus Before, EmergencyReserveStatus After) : ToggleReserveResult;
}

/// <summary><c>PATCH /api/agendamento/&lt;id&gt;/reserva</c> (spec 011 §3.4): reserva antes e depois pela 005 (desvio 014).</summary>
public sealed class ToggleReserveSchedule(
    IUserApiKeyAuthenticator authenticator, IReserveScheduleStore store, IEmergencyReserveService reserve)
{
    public async Task<ToggleReserveResult> ExecuteAsync(ReserveToggleParse parsed, int scheduleId, CancellationToken cancellationToken)
    {
        if (parsed is not ReserveToggleParse.Ok input)
        {
            return new ToggleReserveResult.MissingFields();
        }

        var user = await authenticator.AuthenticateAsync(input.ApiKey, cancellationToken);
        if (user is null)
        {
            return new ToggleReserveResult.Unauthorized();
        }

        var before = await reserve.GetStatusAsync(user.UserId, cancellationToken);
        var schedule = await store.FindActiveScheduleAsync(user.UserId, scheduleId, cancellationToken);
        if (schedule is null)
        {
            return new ToggleReserveResult.NotFound();
        }

        // O Flask passa `incluir` ao driver sem checar o tipo: o que não é booleano falha no UPDATE (desvio 097).
        var include = input.Include ?? throw new AutomationFailureException("incluir não é booleano");
        await store.SetIncludeInReserveAsync(user.UserId, scheduleId, include, cancellationToken);
        var after = await reserve.GetStatusAsync(user.UserId, cancellationToken);
        return new ToggleReserveResult.Updated(schedule, include, before, after);
    }
}

public abstract record ListReserveResult
{
    public sealed record MissingKey : ListReserveResult;

    public sealed record Unauthorized : ListReserveResult;

    public sealed record InvalidPeriodicity : ListReserveResult;

    public sealed record Listed(ReserveListFilter Filter, ReserveListPage Page, EmergencyReserveStatus Status) : ListReserveResult;
}

/// <param name="Key"><c>api_key</c> da query (nulo ou vazio = 400); cada filtro é nulo quando a chave não veio.</param>
public sealed record ListReserveInput(string? Key, string? Include, string? Periodicity, string? Category, string? Limit, string? Offset);

/// <summary><c>GET /api/agendamentos/reserva</c> (spec 011 §3.5).</summary>
public sealed class ListReserveSchedules(
    IUserApiKeyAuthenticator authenticator, IReserveScheduleStore store, IEmergencyReserveService reserve)
{
    public async Task<ListReserveResult> ExecuteAsync(ListReserveInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(input.Key))
        {
            return new ListReserveResult.MissingKey();
        }

        var user = await authenticator.AuthenticateAsync(input.Key, cancellationToken);
        if (user is null)
        {
            return new ListReserveResult.Unauthorized();
        }

        var parsed = ReserveListQuery.Parse(input.Include, input.Periodicity, input.Category, input.Limit, input.Offset);
        switch (parsed)
        {
            case ReserveListParse.InvalidPeriodicity:
                return new ListReserveResult.InvalidPeriodicity();
            case ReserveListParse.InvalidPaging:
                throw new AutomationFailureException("limit/offset inválidos");
        }

        var filter = ((ReserveListParse.Ok)parsed).Filter;
        var page = await store.ListAsync(user.UserId, filter, cancellationToken);
        var status = await reserve.GetStatusAsync(user.UserId, cancellationToken);
        return new ListReserveResult.Listed(filter, page, status);
    }
}

public abstract record ReprocessInvoiceResult
{
    public sealed record NotFound(int InvoiceId) : ReprocessInvoiceResult;

    public sealed record Processed(ReprocessTarget Target, InvoiceScheduleStats Stats) : ReprocessInvoiceResult;
}

/// <summary><c>POST /api/fatura/&lt;id&gt;/reprocessar</c> (spec 011 §3.6; a autenticação por <c>x-api-key</c> é do filtro da rota).</summary>
public sealed class ReprocessInvoice(IInvoiceScheduleUnitOfWork unitOfWork, IInvoiceScheduleProcessor processor)
{
    public async Task<ReprocessInvoiceResult> ExecuteAsync(int invoiceId, CancellationToken cancellationToken)
    {
        var target = await unitOfWork.FindInvoiceAsync(invoiceId, cancellationToken);
        if (target is null)
        {
            return new ReprocessInvoiceResult.NotFound(invoiceId);
        }

        var stats = await processor.ProcessAsync(target.InvoiceId, target.CardAccountId, target.DueDate, cancellationToken);
        return new ReprocessInvoiceResult.Processed(target, stats);
    }
}
