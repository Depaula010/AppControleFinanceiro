using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;

namespace MeuSecretario.Domain.Bills;

/// <summary>Dados validados do POST. A conta ativa do usuário e a subcategoria visível são checadas no banco.</summary>
public sealed record NewBillData(
    string Description,
    decimal? Amount,
    string Type,
    string Periodicity,
    int Day,
    int? Month,
    int NotifyDaysBefore,
    DateOnly StartDate,
    bool IncludeInReserve,
    int? TotalInstallments,
    int SubcategoryId,
    int AccountId);

public abstract record CreateBillValidation
{
    public sealed record Ok(NewBillData Data) : CreateBillValidation;

    public sealed record Rejected(BillError Reason) : CreateBillValidation;
}

/// <summary>Leitura de campos comuns ao POST e ao PUT.</summary>
internal static class BillFields
{
    public const int DefaultNotifyDays = 3;

    public static bool TryDay(RawValue raw, out int day) =>
        PyNumber.TryInt(raw, out day) && day is >= 1 and <= 31;

    public static bool TryMonth(RawValue raw, out int month) =>
        PyNumber.TryInt(raw, out month) && month is >= 1 and <= 12;

    public static bool IsAbsentOrNull(RawValue raw) => raw is RawValue.AbsentValue or RawValue.NullValue;

    /// <summary>Data <c>YYYY-MM-DD</c> (desvio 072); qualquer outro tipo de valor é inválido.</summary>
    public static bool TryStartDate(RawValue raw, out DateOnly date)
    {
        date = default;
        return raw is RawValue.TextValue text && FlaskDate.TryParse(text.Value, out date);
    }
}

/// <summary>Validações do POST na ordem da spec 005 §3.2 (linhas 1 a 11, mais as novas dos desvios 072 e 074).</summary>
public static class CreateBillRules
{
    public static CreateBillValidation Validate(RawBody? body, DateOnly today)
    {
        // 1
        if (body is null || body.IsEmpty)
        {
            return Fail(BillError.DataMissing);
        }

        // 2 (desvio 072: não texto conta como ausente) e Q-005-5 (mais de 255 é recusado)
        var description = body.Get("descricao") is RawValue.TextValue d ? d.Value.Trim() : string.Empty;
        if (description.Length == 0)
        {
            return Fail(BillError.DescriptionRequired);
        }
        if (!BillDescription.Fits(description))
        {
            return Fail(BillError.DescriptionTooLong);
        }

        // 3, 4
        var type = body.Get("tipo_agendamento") is RawValue.TextValue t ? t.Value.Trim() : string.Empty;
        if (!BillTypes.IsValid(type))
        {
            return Fail(BillError.TypeInvalidCreate);
        }
        var periodicity = body.Get("periodicidade") is RawValue.TextValue p ? p.Value.Trim() : string.Empty;
        if (!Periodicities.IsValid(periodicity))
        {
            return Fail(BillError.PeriodicityInvalidCreate);
        }

        // 5, 6
        var subcategory = body.Get("subcategoria_id");
        if (RawFields.IsFalsy(subcategory))
        {
            return Fail(BillError.SubcategoryRequired);
        }
        var account = body.Get("conta_id");
        if (RawFields.IsFalsy(account))
        {
            return Fail(BillError.AccountRequired);
        }

        // 7
        if (!BillFields.TryDay(body.Get("dia_execucao"), out var day))
        {
            return Fail(BillError.DayInvalid);
        }

        // 8, 9
        decimal? amount = null;
        var rawAmount = body.Get("valor_previsto");
        if (BillTypes.RequiresAmount(type))
        {
            if (BillFields.IsAbsentOrNull(rawAmount))
            {
                return Fail(BillError.AmountRequired);
            }
            if (!PyNumber.TryMoney(rawAmount, out var money) || money <= 0)
            {
                return Fail(BillError.AmountNotPositive);
            }
            amount = money;
        }
        else if (!BillFields.IsAbsentOrNull(rawAmount) && PyNumber.TryMoney(rawAmount, out var optional))
        {
            // LEMBRETE_VARIAVEL: valor ilegível vira NULL sem erro; zero e negativo são aceitos.
            amount = optional;
        }

        // 10
        int? installments = null;
        if (type == BillTypes.Installment)
        {
            if (!PyNumber.TryInt(body.Get("total_parcelas"), out var total) || total < 2)
            {
                return Fail(BillError.InstallmentsInvalid);
            }
            installments = total;
        }

        // 11 e desvio 074: ANUAL sem mês não viola mais o CHECK, é 400
        int? month = null;
        if (periodicity == Periodicities.Annual)
        {
            var rawMonth = body.Get("mes_execucao");
            if (BillFields.IsAbsentOrNull(rawMonth))
            {
                return Fail(BillError.MonthRequiredForAnnual);
            }
            if (!BillFields.TryMonth(rawMonth, out var parsedMonth))
            {
                return Fail(BillError.MonthInvalidCreate);
            }
            month = parsedMonth;
        }

        // desvio 072: data_inicio e incluir_na_reserva ilegíveis deixam de ser 500
        var startDate = today;
        var rawStart = body.Get("data_inicio");
        if (!RawFields.IsFalsy(rawStart))
        {
            if (!BillFields.TryStartDate(rawStart, out startDate))
            {
                return Fail(BillError.StartDateInvalid);
            }
        }

        var include = false;
        switch (body.Get("incluir_na_reserva"))
        {
            case RawValue.AbsentValue or RawValue.NullValue:
                break;
            case RawValue.BoolValue flag:
                include = flag.Value;
                break;
            default:
                return Fail(BillError.ReserveFlagInvalid);
        }

        var notify = PyNumber.TryInt(body.Get("notificar_antes_dias"), out var days) ? days : BillFields.DefaultNotifyDays;

        return new CreateBillValidation.Ok(new NewBillData(
            description, amount, type, periodicity, day, month, notify, startDate, include, installments,
            RawFields.ToId(subcategory), RawFields.ToId(account)));
    }

    private static CreateBillValidation.Rejected Fail(BillError error) => new(error);
}
