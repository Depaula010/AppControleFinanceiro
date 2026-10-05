using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Domain.Bills;

/// <summary>Campos do PUT: <see cref="Change{T}.IsSet"/> distingue "não veio" de "veio <c>null</c>".</summary>
public sealed record BillChanges(
    Change<string> Description,
    Change<string> Type,
    Change<string> Periodicity,
    Change<int> Day,
    Change<int?> Month,
    Change<decimal?> Amount,
    Change<int> SubcategoryId,
    Change<int> AccountId,
    Change<int> NotifyDaysBefore,
    Change<DateOnly> StartDate,
    Change<bool> IncludeInReserve,
    Change<int?> TotalInstallments,
    Change<int> ExecutedInstallments)
{
    public bool IsEmpty =>
        !(Description.IsSet || Type.IsSet || Periodicity.IsSet || Day.IsSet || Month.IsSet || Amount.IsSet
          || SubcategoryId.IsSet || AccountId.IsSet || NotifyDaysBefore.IsSet || StartDate.IsSet
          || IncludeInReserve.IsSet || TotalInstallments.IsSet || ExecutedInstallments.IsSet);
}

/// <summary>Descrição, tipo, periodicidade, dia, mês e valor (linhas 3 a 8 da spec §3.3).</summary>
public sealed record BillHeadChanges(
    Change<string> Description,
    Change<string> Type,
    Change<string> Periodicity,
    Change<int> Day,
    Change<int?> Month,
    Change<decimal?> Amount);

/// <summary>Ids crus do corpo para o caso de uso conferir no banco (<c>0</c> nunca existe).</summary>
public sealed record BillReferences(int? SubcategoryId, int? AccountId);

public abstract record BillHeadResult
{
    public sealed record Ok(BillHeadChanges Head) : BillHeadResult;

    public sealed record Rejected(BillError Reason) : BillHeadResult;
}

public abstract record BillTailResult
{
    public sealed record Ok(BillChanges Changes) : BillTailResult;

    public sealed record Rejected(BillError Reason) : BillTailResult;
}

/// <summary>
/// Passos do PUT (spec 005 §3.3). O caso de uso os encadeia na ordem da spec porque o banco entra no meio
/// (404 do agendamento antes dos campos; subcategoria e conta entre o valor e o restante).
/// </summary>
public static class UpdateBillRules
{
    /// <summary>Linhas 3 a 8.</summary>
    public static BillHeadResult ReadHead(RawBody body)
    {
        var description = default(Change<string>);
        var rawDescription = body.Get("descricao");
        if (rawDescription is not RawValue.AbsentValue)
        {
            // Desvio 072: null e não texto deixam de gravar "None".
            var trimmed = rawDescription is RawValue.TextValue text ? text.Value.Trim() : string.Empty;
            if (trimmed.Length == 0)
            {
                return Reject(BillError.DescriptionEmpty);
            }
            if (!BillDescription.Fits(trimmed))
            {
                return Reject(BillError.DescriptionTooLong);
            }
            description = Change.Set(trimmed);
        }

        var type = default(Change<string>);
        var rawType = body.Get("tipo_agendamento");
        if (rawType is not RawValue.AbsentValue)
        {
            // Sem trim no PUT: " FIXO " é inválido.
            if (rawType is not RawValue.TextValue typeText || !BillTypes.IsValid(typeText.Value))
            {
                return Reject(BillError.TypeInvalidUpdate);
            }
            type = Change.Set(typeText.Value);
        }

        var periodicity = default(Change<string>);
        var rawPeriodicity = body.Get("periodicidade");
        if (rawPeriodicity is not RawValue.AbsentValue)
        {
            if (rawPeriodicity is not RawValue.TextValue periodText || !Periodicities.IsValid(periodText.Value))
            {
                return Reject(BillError.PeriodicityInvalidUpdate);
            }
            periodicity = Change.Set(periodText.Value);
        }

        var day = default(Change<int>);
        var rawDay = body.Get("dia_execucao");
        if (rawDay is not RawValue.AbsentValue)
        {
            if (!BillFields.TryDay(rawDay, out var parsedDay))
            {
                return Reject(BillError.DayInvalid);
            }
            day = Change.Set(parsedDay);
        }

        var month = default(Change<int?>);
        var rawMonth = body.Get("mes_execucao");
        if (rawMonth is RawValue.NullValue)
        {
            month = Change.Set<int?>(null);
        }
        else if (rawMonth is not RawValue.AbsentValue)
        {
            if (!BillFields.TryMonth(rawMonth, out var parsedMonth))
            {
                return Reject(BillError.MonthInvalidUpdate);
            }
            month = Change.Set<int?>(parsedMonth);
        }

        var amount = default(Change<decimal?>);
        var rawAmount = body.Get("valor_previsto");
        if (rawAmount is RawValue.NullValue)
        {
            amount = Change.Set<decimal?>(null);
        }
        else if (rawAmount is not RawValue.AbsentValue)
        {
            // Sem validação de sinal (zero e negativo aceitos); não finito ou fora de numeric(15,2) é inválido (desvio 072).
            if (!PyNumber.TryMoney(rawAmount, out var money))
            {
                return Reject(BillError.AmountInvalidUpdate);
            }
            amount = Change.Set<decimal?>(money);
        }

        return new BillHeadResult.Ok(new BillHeadChanges(description, type, periodicity, day, month, amount));
    }

    /// <summary>Linhas 9 e 10: ids para o caso de uso conferir (subcategoria visível, conta ativa do usuário).</summary>
    public static BillReferences ReadReferences(RawBody body) => new(
        body.Has("subcategoria_id") ? RawFields.ToId(body.Get("subcategoria_id")) : null,
        body.Has("conta_id") ? RawFields.ToId(body.Get("conta_id")) : null);

    /// <summary>Linhas 11 a 15; devolve as mudanças completas (a linha 16, "nada a atualizar", é <see cref="BillChanges.IsEmpty"/>).</summary>
    public static BillTailResult ReadTail(RawBody body, BillHeadChanges head, BillReferences references)
    {
        // 11 (desvio 072): notificar_antes_dias ilegível é ignorado, como o código do Flask pretendia.
        var notify = default(Change<int>);
        if (body.Has("notificar_antes_dias") && PyNumber.TryInt(body.Get("notificar_antes_dias"), out var days))
        {
            notify = Change.Set(days);
        }

        // 12 (desvio 072): só YYYY-MM-DD; null também é recusado.
        var start = default(Change<DateOnly>);
        if (body.Has("data_inicio"))
        {
            if (!BillFields.TryStartDate(body.Get("data_inicio"), out var date))
            {
                return RejectTail(BillError.StartDateInvalid);
            }
            start = Change.Set(date);
        }

        // 13 (desvio 072): só booleano.
        var include = default(Change<bool>);
        if (body.Has("incluir_na_reserva"))
        {
            if (body.Get("incluir_na_reserva") is not RawValue.BoolValue flag)
            {
                return RejectTail(BillError.ReserveFlagInvalid);
            }
            include = Change.Set(flag.Value);
        }

        // 14
        var installments = default(Change<int?>);
        var rawInstallments = body.Get("total_parcelas");
        if (rawInstallments is RawValue.NullValue)
        {
            installments = Change.Set<int?>(null);
        }
        else if (rawInstallments is not RawValue.AbsentValue)
        {
            if (!PyNumber.TryInt(rawInstallments, out var total) || total < 2)
            {
                return RejectTail(BillError.InstallmentsInvalid);
            }
            installments = Change.Set<int?>(total);
        }

        // 15: o tipo já gravado não conta, só o que veio no corpo.
        if (head.Type is { IsSet: true, Value: BillTypes.Installment } && !body.Has("total_parcelas"))
        {
            return RejectTail(BillError.InstallmentsRequired);
        }

        return new BillTailResult.Ok(new BillChanges(
            head.Description,
            head.Type,
            head.Periodicity,
            head.Day,
            head.Month,
            head.Amount,
            references.SubcategoryId is { } subcategory ? Change.Set(subcategory) : default,
            references.AccountId is { } account ? Change.Set(account) : default,
            notify,
            start,
            include,
            installments,
            default));
    }

    private static BillHeadResult.Rejected Reject(BillError error) => new(error);

    private static BillTailResult.Rejected RejectTail(BillError error) => new(error);
}
