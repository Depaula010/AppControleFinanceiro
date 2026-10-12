using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Bills;

namespace MeuSecretario.Application.Bills;

/// <summary><c>GET /api/bills</c> (<c>api.py:1795-1858</c>).</summary>
public sealed class ListBills(IBillRepository bills)
{
    public Task<IReadOnlyList<BillListItem>> ExecuteAsync(int userId, CancellationToken cancellationToken) =>
        bills.ListActiveAsync(userId, cancellationToken);
}

/// <summary><c>DELETE /api/bills/{id}</c> (<c>api.py:2206-2228</c>): uma única instrução, sem leitura prévia.</summary>
public sealed class DeleteBill(IBillRepository bills)
{
    /// <returns><c>true</c> se removeu; <c>false</c> se não há agendamento ativo do usuário (404).</returns>
    public Task<bool> ExecuteAsync(int userId, int billId, CancellationToken cancellationToken) =>
        bills.DeactivateAsync(userId, billId, cancellationToken);
}

/// <summary>
/// <c>POST /api/bills</c> (<c>api.py:1861-2037</c>): validações da spec §3.2 no Domain, depois a conta (linha 12) e,
/// com o desvio 073, a subcategoria.
/// </summary>
public sealed class CreateBill(IBillRepository bills, IClock clock)
{
    public async Task<CreateBillResult> ExecuteAsync(int userId, RawBody? body, CancellationToken cancellationToken)
    {
        var validation = CreateBillRules.Validate(body, clock.Today);
        if (validation is CreateBillValidation.Rejected rejected)
        {
            return new CreateBillResult.Invalid(rejected.Reason);
        }

        var data = ((CreateBillValidation.Ok)validation).Data;
        if (!await bills.ActiveAccountOwnedAsync(userId, data.AccountId, cancellationToken))
        {
            return new CreateBillResult.AccountNotFound();
        }

        if (!await bills.SubcategoryVisibleAsync(userId, data.SubcategoryId, cancellationToken))
        {
            return new CreateBillResult.SubcategoryNotFound();
        }

        return new CreateBillResult.Created(await bills.CreateAsync(userId, data, cancellationToken));
    }
}

/// <summary>
/// <c>PUT /api/bills/{id}</c> (<c>api.py:2040-2203</c>). A ordem é a da spec §3.3: corpo, 404, campos (descrição a valor),
/// subcategoria (desvio 073), conta, restante, "nenhum campo", consistência do estado final (desvio 074), gravação.
/// </summary>
public sealed class UpdateBill(IBillRepository bills)
{
    public async Task<UpdateBillResult> ExecuteAsync(int userId, int billId, RawBody? body, CancellationToken cancellationToken)
    {
        if (body is null || body.IsEmpty)
        {
            return new UpdateBillResult.Invalid(BillError.DataMissing);
        }

        var current = await bills.GetCurrentAsync(userId, billId, cancellationToken);
        if (current is null)
        {
            return new UpdateBillResult.NotFound();
        }

        var headResult = UpdateBillRules.ReadHead(body);
        if (headResult is BillHeadResult.Rejected headRejected)
        {
            return new UpdateBillResult.Invalid(headRejected.Reason);
        }

        var head = (BillHeadResult.Ok)headResult;
        var references = UpdateBillRules.ReadReferences(body);
        if (references.SubcategoryId is { } subcategory
            && !await bills.SubcategoryVisibleAsync(userId, subcategory, cancellationToken))
        {
            return new UpdateBillResult.SubcategoryNotFound();
        }

        if (references.AccountId is { } account
            && !await bills.ActiveAccountOwnedAsync(userId, account, cancellationToken))
        {
            return new UpdateBillResult.AccountNotFound();
        }

        var tail = UpdateBillRules.ReadTail(body, head.Head, references);
        if (tail is BillTailResult.Rejected rejected)
        {
            return new UpdateBillResult.Invalid(rejected.Reason);
        }

        var changes = ((BillTailResult.Ok)tail).Changes;
        if (changes.IsEmpty)
        {
            return new UpdateBillResult.Invalid(BillError.NothingToUpdate);
        }

        switch (BillConsistency.Check(current, changes))
        {
            case ConsistencyResult.Rejected inconsistent:
                return new UpdateBillResult.Invalid(inconsistent.Reason);
            case ConsistencyResult.Ok consistent:
                changes = consistent.Changes;
                break;
        }

        return await bills.UpdateAsync(userId, billId, changes, cancellationToken) is { } updated
            ? new UpdateBillResult.Updated(updated)
            : new UpdateBillResult.NotFound();
    }
}

/// <summary><c>GET /api/bills/summary</c> (<c>api.py:2264-2387</c>): a reserva vem do serviço único (desvio 014).</summary>
public sealed class GetBillsSummary(IBillRepository bills, IEmergencyReserveService reserve)
{
    public async Task<BillsSummary> ExecuteAsync(int userId, CancellationToken cancellationToken)
    {
        var rows = await bills.ListSummaryRowsAsync(userId, cancellationToken);
        var status = await reserve.GetStatusAsync(userId, cancellationToken);
        return BillsSummaryCalculator.Calculate(rows, status.Total, status.Months);
    }
}

/// <summary>Implementação única da reserva de emergência (spec 005 §7; RN-005-11).</summary>
public sealed class EmergencyReserveService(IBillRepository bills) : IEmergencyReserveService
{
    public async Task<EmergencyReserveStatus> GetStatusAsync(int userId, CancellationToken cancellationToken)
    {
        var months = await bills.GetReserveMonthsAsync(userId, cancellationToken);
        var totals = await bills.ListReserveTotalsAsync(userId, cancellationToken);
        var result = EmergencyReserve.Calculate(months, totals);
        return new EmergencyReserveStatus(result.MonthlyEquivalent, result.Total, months);
    }

    public async Task<IReadOnlyList<EmergencyReserveItem>> GetItemsAsync(int userId, CancellationToken cancellationToken)
    {
        var months = await bills.GetReserveMonthsAsync(userId, cancellationToken);
        var rows = await bills.ListReserveItemsAsync(userId, cancellationToken);

        return rows
            .Select(row =>
            {
                var amount = (double)row.Amount;
                return new EmergencyReserveItem(
                    row.ScheduleId, row.Description, amount, row.Periodicity, row.Category,
                    EmergencyReserve.Impact(amount, row.Periodicity, months));
            })
            .OrderByDescending(item => item.Impact)
            .ThenBy(item => item.ScheduleId)
            .ToList();
    }
}
