using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Accounts;

public sealed record NewAccountData(
    string NomeConta,
    string TipoConta,
    decimal SaldoInicial,
    string? CorHex,
    string? Icone,
    decimal? LimiteCredito,
    int? DiaFechamento,
    int? DiaVencimento);

/// <summary>Um campo do PUT: <see cref="IsSet"/> distingue "não veio" de "veio <c>null</c>" (RN-002-13).</summary>
public readonly record struct Change<T>(bool IsSet, T Value);

public static class Change
{
    public static Change<T> Set<T>(T value) => new(true, value);
}

public sealed record AccountChanges(
    Change<string> Nome,
    Change<string> Tipo,
    Change<decimal> Saldo,
    Change<string?> Cor,
    Change<string?> Icone,
    Change<decimal?> Limite,
    Change<int?> Fechamento,
    Change<int?> Vencimento)
{
    public bool IsEmpty =>
        !(Nome.IsSet || Tipo.IsSet || Saldo.IsSet || Cor.IsSet || Icone.IsSet || Limite.IsSet || Fechamento.IsSet || Vencimento.IsSet);

    public AccountChanges WithName(string name) => this with { Nome = Change.Set<string>(name) };
}

public abstract record CreateValidation
{
    public sealed record Ok(NewAccountData Data) : CreateValidation;

    public sealed record Rejected(AccountError Reason) : CreateValidation;
}

public abstract record NameStep
{
    public sealed record NotPresent : NameStep;

    public sealed record Value(string Name) : NameStep;

    public sealed record Failed(AccountError Reason) : NameStep;
}

public abstract record FieldsResult
{
    public sealed record Ok(AccountChanges Changes) : FieldsResult;

    public sealed record Rejected(AccountError Reason) : FieldsResult;
}

internal static partial class AccountFields
{
    public const string DefaultColor = "#3B82F6";
    public const string DefaultIcon = "wallet";
    public const int MaxIconLength = 50;
    public const int MinDay = 1;
    public const int MaxDay = 31;

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    public static bool IsValidColor(string value) => ColorPattern().IsMatch(value);

    public static bool IsValidIcon(string value) => AccountName.CountRunes(value) <= MaxIconLength;

    /// <summary>Dia 1..31 convertido como o <c>int()</c> do Python.</summary>
    public static bool TryDay(RawValue raw, out int day) =>
        PyNumber.TryInt(raw, out day) && day is >= MinDay and <= MaxDay;

    public static bool IsPresentAndNotNull(RawValue raw) => raw is not (RawValue.AbsentValue or RawValue.NullValue);
}

/// <summary>Validações do POST, na ordem da spec 002 §3.2 (linhas 1 a 12). A linha 13 (nome ativo em uso) é do banco.</summary>
public static class CreateRules
{
    public static CreateValidation Validate(RawBody? body)
    {
        if (body is null || body.IsEmpty)
        {
            return Fail(AccountError.DataMissing);
        }

        // Desvio 033: nome/tipo que não são texto contam como ausentes.
        var nome = body.Get("nome_conta") is RawValue.TextValue n ? n.Value.Trim() : string.Empty;
        if (nome.Length == 0)
        {
            return Fail(AccountError.NameRequired);
        }

        var tipo = body.Get("tipo_conta") is RawValue.TextValue t ? t.Value.Trim() : string.Empty;
        if (tipo.Length == 0)
        {
            return Fail(AccountError.TypeRequired);
        }

        if (!AccountTypes.IsValid(tipo))
        {
            return Fail(AccountError.TypeInvalid);
        }

        var saldo = 0m;
        if (AccountFields.IsPresentAndNotNull(body.Get("saldo_inicial")) && !PyNumber.TryMoney(body.Get("saldo_inicial"), out saldo))
        {
            return Fail(AccountError.BalanceInvalid);
        }

        if (!TryColor(body.Get("cor_hex"), AccountFields.DefaultColor, out var cor))
        {
            return Fail(AccountError.ColorInvalid);
        }

        if (!TryIcon(body.Get("icone"), AccountFields.DefaultIcon, out var icone))
        {
            return Fail(AccountError.IconInvalid);
        }

        decimal? limite = null;
        if (AccountFields.IsPresentAndNotNull(body.Get("limite_credito")))
        {
            if (!PyNumber.TryMoney(body.Get("limite_credito"), out var parsedLimit))
            {
                return Fail(AccountError.LimitInvalid);
            }
            limite = parsedLimit;
        }

        var closing = body.Get("dia_fechamento");
        var due = body.Get("dia_vencimento");
        int? fechamento = null;
        int? vencimento = null;

        if (tipo != AccountTypes.Card)
        {
            if (AccountFields.IsPresentAndNotNull(closing) || AccountFields.IsPresentAndNotNull(due))
            {
                return Fail(AccountError.DaysOnlyForCard);
            }
        }
        else
        {
            if (AccountFields.IsPresentAndNotNull(closing))
            {
                if (!AccountFields.TryDay(closing, out var day))
                {
                    return Fail(AccountError.ClosingDayInvalid);
                }
                fechamento = day;
            }

            if (AccountFields.IsPresentAndNotNull(due))
            {
                if (!AccountFields.TryDay(due, out var day))
                {
                    return Fail(AccountError.DueDayInvalid);
                }
                vencimento = day;
            }

            if (fechamento is null || vencimento is null)
            {
                return Fail(AccountError.CardNeedsDays);
            }
        }

        return new CreateValidation.Ok(new NewAccountData(
            AccountName.Truncate(nome), tipo, saldo, cor, icone, limite, fechamento, vencimento));
    }

    private static CreateValidation.Rejected Fail(AccountError error) => new CreateValidation.Rejected(error);

    private static bool TryColor(RawValue raw, string whenAbsent, out string? color)
    {
        color = whenAbsent;
        switch (raw)
        {
            case RawValue.AbsentValue:
                return true;
            case RawValue.NullValue:
                color = null;
                return true;
            case RawValue.TextValue text when AccountFields.IsValidColor(text.Value):
                color = text.Value;
                return true;
            default:
                return false;
        }
    }

    private static bool TryIcon(RawValue raw, string whenAbsent, out string? icon)
    {
        icon = whenAbsent;
        switch (raw)
        {
            case RawValue.AbsentValue:
                return true;
            case RawValue.NullValue:
                icon = null;
                return true;
            case RawValue.TextValue text when AccountFields.IsValidIcon(text.Value):
                icon = text.Value;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Passos do PUT (spec 002 §3.3). O caso de uso os encadeia na ordem da spec porque o banco entra no meio
/// (404 antes dos campos; duplicidade de nome entre o nome e o tipo).
/// </summary>
public static class UpdateRules
{
    public static NameStep ReadName(RawBody body)
    {
        var raw = body.Get("nome_conta");
        if (raw is RawValue.AbsentValue)
        {
            return new NameStep.NotPresent();
        }

        // Desvio 033: nome que não é texto (inclui null) é tratado como vazio.
        if (raw is not RawValue.TextValue text)
        {
            return new NameStep.Failed(AccountError.NameEmpty);
        }

        var name = AccountName.Normalize(text.Value);
        return name.Length == 0 ? new NameStep.Failed(AccountError.NameEmpty) : new NameStep.Value(name);
    }

    /// <summary>Tipo, saldo, cor, ícone, limite, fechamento e vencimento, nessa ordem; o nome fica de fora.</summary>
    public static FieldsResult ReadFields(RawBody body)
    {
        var tipo = default(Change<string>);
        var tipoRaw = body.Get("tipo_conta");
        if (tipoRaw is not RawValue.AbsentValue)
        {
            if (tipoRaw is not RawValue.TextValue tipoText || !AccountTypes.IsValid(tipoText.Value.Trim()))
            {
                return Fail(AccountError.TypeInvalid);
            }
            tipo = Change.Set<string>(tipoText.Value.Trim());
        }

        var saldo = default(Change<decimal>);
        var saldoRaw = body.Get("saldo_inicial");
        if (saldoRaw is not RawValue.AbsentValue)
        {
            if (!PyNumber.TryMoney(saldoRaw, out var money))
            {
                return Fail(AccountError.BalanceInvalid);
            }
            saldo = Change.Set<decimal>(money);
        }

        var cor = default(Change<string?>);
        switch (body.Get("cor_hex"))
        {
            case RawValue.AbsentValue:
                break;
            case RawValue.NullValue:
                cor = Change.Set<string?>(null);
                break;
            case RawValue.TextValue text when AccountFields.IsValidColor(text.Value):
                cor = Change.Set<string?>(text.Value);
                break;
            default:
                return Fail(AccountError.ColorInvalid);
        }

        var icone = default(Change<string?>);
        switch (body.Get("icone"))
        {
            case RawValue.AbsentValue:
                break;
            case RawValue.NullValue:
                icone = Change.Set<string?>(null);
                break;
            case RawValue.TextValue text when AccountFields.IsValidIcon(text.Value):
                icone = Change.Set<string?>(text.Value);
                break;
            default:
                return Fail(AccountError.IconInvalid);
        }

        var limite = default(Change<decimal?>);
        var limiteRaw = body.Get("limite_credito");
        if (limiteRaw is RawValue.NullValue)
        {
            limite = Change.Set<decimal?>(null);
        }
        else if (limiteRaw is not RawValue.AbsentValue)
        {
            if (!PyNumber.TryMoney(limiteRaw, out var parsedLimit))
            {
                return Fail(AccountError.LimitInvalid);
            }
            limite = Change.Set<decimal?>(parsedLimit);
        }

        if (!TryDayChange(body.Get("dia_fechamento"), out var fechamento))
        {
            return Fail(AccountError.ClosingDayInvalid);
        }

        if (!TryDayChange(body.Get("dia_vencimento"), out var vencimento))
        {
            return Fail(AccountError.DueDayInvalid);
        }

        return new FieldsResult.Ok(new AccountChanges(
            default(Change<string>), tipo, saldo, cor, icone, limite, fechamento, vencimento));
    }

    /// <summary>
    /// Consistência com a conta atual (spec §3.3 linhas 13 e 14; desvios 034 e 036). Só vale quando o corpo toca o tipo
    /// ou algum dia: nada é limpo em silêncio e um cartão antigo sem dias ainda pode ser renomeado.
    /// </summary>
    public static AccountError? CheckConsistency(string currentType, int? currentClosing, int? currentDue, AccountChanges changes)
    {
        if (!(changes.Tipo.IsSet || changes.Fechamento.IsSet || changes.Vencimento.IsSet))
        {
            return null;
        }

        var type = changes.Tipo.IsSet ? changes.Tipo.Value : currentType;
        var closing = changes.Fechamento.IsSet ? changes.Fechamento.Value : currentClosing;
        var due = changes.Vencimento.IsSet ? changes.Vencimento.Value : currentDue;

        if (type != AccountTypes.Card)
        {
            return closing is not null || due is not null ? AccountError.DaysOnlyForCard : null;
        }

        return closing is null || due is null ? AccountError.CardNeedsDays : null;
    }

    private static FieldsResult.Rejected Fail(AccountError error) => new FieldsResult.Rejected(error);

    private static bool TryDayChange(RawValue raw, out Change<int?> change)
    {
        change = default(Change<int?>);
        switch (raw)
        {
            case RawValue.AbsentValue:
                return true;
            case RawValue.NullValue:
                change = Change.Set<int?>(null);
                return true;
            default:
                if (!AccountFields.TryDay(raw, out var day))
                {
                    return false;
                }
                change = Change.Set<int?>(day);
                return true;
        }
    }
}
