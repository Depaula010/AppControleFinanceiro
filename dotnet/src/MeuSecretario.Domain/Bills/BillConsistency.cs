using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Domain.Bills;

/// <summary>Estado gravado do agendamento, para conferir o estado resultante do PUT (desvio 074).</summary>
public sealed record CurrentBill(
    string Type,
    string Periodicity,
    int? Month,
    decimal? Amount,
    int? TotalInstallments,
    int ExecutedInstallments);

public abstract record ConsistencyResult
{
    public sealed record Ok(BillChanges Changes) : ConsistencyResult;

    public sealed record Rejected(BillError Reason) : ConsistencyResult;
}

/// <summary>
/// Estado <b>resultante</b> (gravado + corpo) contra <c>chk_anual_mes</c>, <c>chk_parcelado</c> e o valor previsto
/// (desvio 074). O que deixa de se aplicar é limpo: mês fora de <c>ANUAL</c>; parcelas e execuções fora de <c>PARCELADO</c>.
/// </summary>
public static class BillConsistency
{
    public static ConsistencyResult Check(CurrentBill current, BillChanges changes)
    {
        var type = changes.Type.IsSet ? changes.Type.Value : current.Type;
        var periodicity = changes.Periodicity.IsSet ? changes.Periodicity.Value : current.Periodicity;
        var month = changes.Month.IsSet ? changes.Month.Value : current.Month;
        var total = changes.TotalInstallments.IsSet ? changes.TotalInstallments.Value : current.TotalInstallments;
        var amount = changes.Amount.IsSet ? changes.Amount.Value : current.Amount;

        var adjusted = changes;

        if (periodicity == Periodicities.Annual)
        {
            if (month is null)
            {
                return new ConsistencyResult.Rejected(BillError.MonthRequiredForAnnual);
            }
        }
        else if (month is not null)
        {
            adjusted = adjusted with { Month = Change.Set<int?>(null) };
        }

        if (type == BillTypes.Installment)
        {
            if (total is { } installments && installments < current.ExecutedInstallments)
            {
                return new ConsistencyResult.Rejected(BillError.InstallmentsBelowExecuted);
            }
        }
        else
        {
            if (total is not null)
            {
                adjusted = adjusted with { TotalInstallments = Change.Set<int?>(null) };
            }
            if (current.ExecutedInstallments != 0)
            {
                adjusted = adjusted with { ExecutedInstallments = Change.Set(0) };
            }
        }

        // Só quando o corpo toca o tipo ou o valor: um agendamento antigo sem valor ainda pode ser renomeado.
        if ((changes.Type.IsSet || changes.Amount.IsSet) && BillTypes.RequiresAmount(type))
        {
            if (amount is null)
            {
                return new ConsistencyResult.Rejected(BillError.AmountRequired);
            }
            if (amount <= 0)
            {
                return new ConsistencyResult.Rejected(BillError.AmountNotPositive);
            }
        }

        return new ConsistencyResult.Ok(adjusted);
    }
}
