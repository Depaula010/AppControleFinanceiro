using System.Globalization;
using System.Numerics;
using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Domain.Transactions;

/// <summary>Erros de validação das 4 rotas (sem texto em português; o endpoint mapeia para a mensagem).</summary>
public enum TransactionError
{
    DescriptionRequired,
    DescriptionTooLong,
    DescriptionInvalid,
    ValueRequired,
    ValueInvalid,
    ValueNegative,
    TypeInvalid,
    DateRequired,
    DateFormat,
    SubcategoryRequired,
    AccountRequired,
    NotesInvalid,
    ConsolidatedInvalid,
    NoDataProvided,
    NoValidField,
    NotEditable,
}

/// <summary>Leitura de campos do corpo com a verdade e as conversões do Python.</summary>
internal static class RawFields
{
    public const int DescriptionMaxLength = 255;

    /// <summary><c>not valor</c> do Python: ausente, <c>null</c>, <c>false</c>, <c>0</c>, <c>0.0</c> e texto vazio.</summary>
    public static bool IsFalsy(RawValue raw) => raw switch
    {
        RawValue.AbsentValue or RawValue.NullValue => true,
        RawValue.BoolValue b => !b.Value,
        RawValue.IntegerValue i => i.Value.IsZero,
        RawValue.DecimalValue d => d.Value == 0d,
        RawValue.TextValue t => t.Value.Length == 0,
        _ => false,
    };

    /// <summary>
    /// Id vindo do JSON: inteiro ou texto inteiro. Qualquer outra coisa (fracionário, booleano, objeto, fora de
    /// <see cref="int"/>) vira 0, que nunca existe (identidade começa em 1) e responde "não encontrado" (desvio 066).
    /// </summary>
    public static int ToId(RawValue raw)
    {
        switch (raw)
        {
            case RawValue.IntegerValue i when i.Value >= int.MinValue && i.Value <= int.MaxValue:
                return (int)i.Value;
            case RawValue.TextValue t
                when BigInteger.TryParse(t.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big)
                     && big >= int.MinValue && big <= int.MaxValue:
                return (int)big;
            default:
                return 0;
        }
    }

    public static bool FitsDescription(string trimmed) => trimmed.EnumerateRunes().Count() <= DescriptionMaxLength;
}

// ---------- GET ----------

public enum ListQueryError
{
    LimitRange,
    OffsetNegative,
    TypeInvalid,
    DateFormat,
}

public sealed record ListFilter(int Limit, long Offset, TransactionKind? Kind, DateOnly? From, DateOnly? To);

public abstract record ListQueryResult
{
    public sealed record Ok(ListFilter Filter) : ListQueryResult;

    public sealed record Rejected(ListQueryError Error) : ListQueryResult;
}

/// <summary>Parâmetros do GET (spec 004 §3.1). Cada parâmetro chega como o primeiro valor da query (<c>null</c> = ausente).</summary>
public static class ListQueryParser
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static ListQueryResult Parse(string? limit, string? offset, string? tipo, string? from, string? to)
    {
        var limitValue = ParseInteger(limit, DefaultLimit);
        if (limitValue < 1 || limitValue > MaxLimit)
        {
            return new ListQueryResult.Rejected(ListQueryError.LimitRange);
        }

        var offsetValue = ParseInteger(offset, 0);
        if (offsetValue < 0)
        {
            return new ListQueryResult.Rejected(ListQueryError.OffsetNegative);
        }

        TransactionKind? kind = null;
        if (!string.IsNullOrEmpty(tipo))
        {
            kind = TransactionTypes.FromApi(tipo);
            if (kind is null)
            {
                return new ListQueryResult.Rejected(ListQueryError.TypeInvalid);
            }
        }

        if (!TryDate(from, out var fromDate) || !TryDate(to, out var toDate))
        {
            return new ListQueryResult.Rejected(ListQueryError.DateFormat);
        }

        var cappedOffset = offsetValue > long.MaxValue ? long.MaxValue : (long)offsetValue;
        return new ListQueryResult.Ok(new ListFilter((int)limitValue, cappedOffset, kind, fromDate, toDate));
    }

    /// <summary><c>request.args.get(nome, padrão, type=int)</c>: texto que <c>int()</c> não converte vira o padrão.</summary>
    private static BigInteger ParseInteger(string? text, int fallback) =>
        !string.IsNullOrEmpty(text)
        && BigInteger.TryParse(
            text,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

    private static bool TryDate(string? text, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (!FlaskDate.TryParse(text, out var parsed))
        {
            return false;
        }

        date = parsed;
        return true;
    }
}

// ---------- POST ----------

/// <param name="Description">Já sem as pontas.</param>
/// <param name="Amount">Módulo informado (≥ 0), sem sinal.</param>
/// <param name="DateText">Texto da data como recebido (eco da resposta).</param>
public sealed record NewTransactionData(
    string Description,
    decimal Amount,
    TransactionKind Kind,
    string DateText,
    DateOnly Date,
    int SubcategoryId,
    int AccountId,
    string? Notes,
    bool Consolidated);

public abstract record CreateTransactionValidation
{
    public sealed record Ok(NewTransactionData Data) : CreateTransactionValidation;

    public sealed record Rejected(TransactionError Error) : CreateTransactionValidation;
}

/// <summary>Validações 1 a 9 da spec 004 §3.2 (as de banco, 10 e 11, são do caso de uso) mais as do desvio 066.</summary>
public static class CreateTransactionRules
{
    public static CreateTransactionValidation Validate(RawBody? body)
    {
        body ??= new RawBody(new Dictionary<string, RawValue>());

        // 1
        if (body.Get("descricao") is not RawValue.TextValue { Value: var rawDescription }
            || rawDescription.Trim() is not { Length: > 0 } description)
        {
            return Reject(TransactionError.DescriptionRequired);
        }
        if (!RawFields.FitsDescription(description))
        {
            return Reject(TransactionError.DescriptionTooLong);
        }

        // 2, 3, 4
        var valor = body.Get("valor");
        if (valor is RawValue.AbsentValue or RawValue.NullValue)
        {
            return Reject(TransactionError.ValueRequired);
        }
        if (!PyNumber.TryMoney(valor, out var amount))
        {
            return Reject(TransactionError.ValueInvalid);
        }
        if (amount < 0)
        {
            return Reject(TransactionError.ValueNegative);
        }

        // 5
        if (body.Get("tipo") is not RawValue.TextValue tipo || TransactionTypes.FromApi(tipo.Value) is not { } kind)
        {
            return Reject(TransactionError.TypeInvalid);
        }

        // 6, 7
        var data = body.Get("data");
        if (RawFields.IsFalsy(data))
        {
            return Reject(TransactionError.DateRequired);
        }
        if (data is not RawValue.TextValue dateText || !FlaskDate.TryParse(dateText.Value, out var date))
        {
            return Reject(TransactionError.DateFormat);
        }

        // 8, 9
        var subcategory = body.Get("subcategoria_id");
        if (RawFields.IsFalsy(subcategory))
        {
            return Reject(TransactionError.SubcategoryRequired);
        }
        var account = body.Get("conta_id");
        if (RawFields.IsFalsy(account))
        {
            return Reject(TransactionError.AccountRequired);
        }

        // desvio 066: o que no Flask é 500 do banco
        string? notes = null;
        switch (body.Get("observacoes"))
        {
            case RawValue.AbsentValue or RawValue.NullValue:
                break;
            case RawValue.TextValue text:
                notes = text.Value.Trim() is { Length: > 0 } trimmed ? trimmed : null;
                break;
            default:
                return Reject(TransactionError.NotesInvalid);
        }

        var consolidated = true;
        switch (body.Get("consolidada"))
        {
            case RawValue.AbsentValue or RawValue.NullValue:
                break;
            case RawValue.BoolValue flag:
                consolidated = flag.Value;
                break;
            default:
                return Reject(TransactionError.ConsolidatedInvalid);
        }

        return new CreateTransactionValidation.Ok(new NewTransactionData(
            description, amount, kind, dateText.Value, date, RawFields.ToId(subcategory), RawFields.ToId(account), notes, consolidated));
    }

    private static CreateTransactionValidation.Rejected Reject(TransactionError error) => new(error);
}

// ---------- PUT ----------

public sealed record HeadChanges(
    Change<string> Description, Change<decimal> Amount, Change<string> Type, Change<DateOnly> Date);

public abstract record HeadResult
{
    public sealed record Ok(HeadChanges Changes) : HeadResult;

    public sealed record Rejected(TransactionError Error) : HeadResult;
}

/// <summary>Referências do corpo do PUT: <c>null</c> = chave ausente; 0 = id que não existe (<c>null</c>, fracionário, etc.).</summary>
public sealed record References(int? SubcategoryId, int? AccountId);

public sealed record TailChanges(Change<string?> Notes, Change<bool> Consolidated);

public abstract record TailResult
{
    public sealed record Ok(TailChanges Changes) : TailResult;

    public sealed record Rejected(TransactionError Error) : TailResult;
}

public sealed record TransactionChanges(
    Change<string> Description,
    Change<decimal> Amount,
    Change<string> Type,
    Change<DateOnly> Date,
    Change<int> SubcategoryId,
    Change<int> AccountId,
    Change<string?> Notes,
    Change<bool> Consolidated)
{
    public bool IsEmpty =>
        !(Description.IsSet || Amount.IsSet || Type.IsSet || Date.IsSet || SubcategoryId.IsSet
          || AccountId.IsSet || Notes.IsSet || Consolidated.IsSet);

    /// <summary>Conta, data ou tipo mudaram: o vínculo de fatura precisa ser recalculado (desvio 069).</summary>
    public bool AffectsInvoice => AccountId.IsSet || Date.IsSet || Type.IsSet;
}

/// <summary>
/// PUT parcial (spec 004 §3.3). A ordem intercala o banco, então as regras vêm em três passos que o caso de uso
/// chama na ordem: cabeça (descrição, valor, tipo, data), referências (subcategoria e conta, checadas no banco) e cauda.
/// </summary>
public static class UpdateTransactionRules
{
    public static HeadResult ReadHead(RawBody body, string currentDbType, decimal currentAmount)
    {
        var description = default(Change<string>);
        var amount = default(Change<decimal>);
        var type = default(Change<string>);
        var date = default(Change<DateOnly>);

        // 3: falsa é ignorada sem erro
        var rawDescription = body.Get("descricao");
        if (!RawFields.IsFalsy(rawDescription))
        {
            if (rawDescription is not RawValue.TextValue text)
            {
                return Reject(TransactionError.DescriptionInvalid);
            }
            var trimmed = text.Value.Trim();
            if (!RawFields.FitsDescription(trimmed))
            {
                return Reject(TransactionError.DescriptionTooLong);
            }
            description = Change.Set(trimmed);
        }

        // 4, 5
        decimal? magnitude = null;
        if (body.Has("valor"))
        {
            if (!PyNumber.TryMoney(body.Get("valor"), out var parsed))
            {
                return Reject(TransactionError.ValueInvalid);
            }
            if (parsed < 0)
            {
                return Reject(TransactionError.ValueNegative);
            }
            magnitude = parsed;
        }

        // 6
        TransactionKind? kind = null;
        if (body.Has("tipo"))
        {
            kind = body.Get("tipo") is RawValue.TextValue tipo ? TransactionTypes.FromApi(tipo.Value) : null;
            if (kind is null)
            {
                return Reject(TransactionError.TypeInvalid);
            }
            type = Change.Set(TransactionTypes.ToDb(kind.Value));
        }

        // sinal (RN-004-13): valor presente usa o módulo informado; só tipo regrava o módulo atual
        if (magnitude is { } given)
        {
            amount = Change.Set(SignedAmount.ForUpdate(currentDbType, kind, given));
        }
        else if (kind is not null)
        {
            amount = Change.Set(SignedAmount.ForUpdate(currentDbType, kind, Math.Abs(currentAmount)));
        }

        // 7
        if (body.Has("data"))
        {
            if (body.Get("data") is not RawValue.TextValue dateText || !FlaskDate.TryParse(dateText.Value, out var parsedDate))
            {
                return Reject(TransactionError.DateFormat);
            }
            date = Change.Set(parsedDate);
        }

        return new HeadResult.Ok(new HeadChanges(description, amount, type, date));
    }

    public static References ReadReferences(RawBody body) => new(
        body.Has("subcategoria_id") ? RawFields.ToId(body.Get("subcategoria_id")) : null,
        body.Has("conta_id") ? RawFields.ToId(body.Get("conta_id")) : null);

    public static TailResult ReadTail(RawBody body)
    {
        var notes = default(Change<string?>);
        var consolidated = default(Change<bool>);

        // 10: vazio, null e falso limpam; texto verdadeiro é aparado; outro tipo verdadeiro é erro (desvio 066)
        if (body.Has("observacoes"))
        {
            var raw = body.Get("observacoes");
            if (RawFields.IsFalsy(raw))
            {
                notes = Change.Set<string?>(null);
            }
            else if (raw is RawValue.TextValue text)
            {
                notes = Change.Set<string?>(text.Value.Trim() is { Length: > 0 } trimmed ? trimmed : null);
            }
            else
            {
                return new TailResult.Rejected(TransactionError.NotesInvalid);
            }
        }

        // 11: só booleano (desvio 066)
        if (body.Has("consolidada"))
        {
            if (body.Get("consolidada") is not RawValue.BoolValue flag)
            {
                return new TailResult.Rejected(TransactionError.ConsolidatedInvalid);
            }
            consolidated = Change.Set(flag.Value);
        }

        return new TailResult.Ok(new TailChanges(notes, consolidated));
    }

    /// <summary>Junta os três passos; as referências já foram conferidas no banco.</summary>
    public static TransactionChanges Combine(HeadChanges head, References references, TailChanges tail) => new(
        head.Description,
        head.Amount,
        head.Type,
        head.Date,
        references.SubcategoryId is { } subcategory ? Change.Set(subcategory) : default,
        references.AccountId is { } account ? Change.Set(account) : default,
        tail.Notes,
        tail.Consolidated);

    private static HeadResult.Rejected Reject(TransactionError error) => new(error);
}
