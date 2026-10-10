using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Llm;

namespace MeuSecretario.Domain.Automation;

/// <summary>Dados já saneados de <c>/api/transacao</c> (<c>transaction_handler.py:208-274</c>).</summary>
public sealed record ApiTransactionData(
    string? UserApiKey,
    double Value,
    string Local,
    string AccountName,
    string PaymentType,
    string? Description)
{
    /// <summary>Texto para a IA e descrição gravada: <c>local</c> ou <c>local - descricao</c>.</summary>
    public string FinalDescription => Description is null ? Local : $"{Local} - {Description}";
}

public abstract record ApiTransactionParse
{
    public sealed record Ok(ApiTransactionData Data) : ApiTransactionParse;

    /// <summary>Campo de texto verdadeiro que não é texto: o Flask quebra em <c>sanitize_input</c> (500); desvio 097: 400 <c>Campo 'x' deve ser texto</c>.</summary>
    public sealed record UnexpectedType(string Field) : ApiTransactionParse;

    /// <summary>Obrigatórios falsos depois do saneamento, na ordem do Flask.</summary>
    public sealed record Missing(IReadOnlyList<string> Fields) : ApiTransactionParse;

    /// <summary>Mensagem completa depois de <c>"Valor invalido: "</c>.</summary>
    public sealed record InvalidValue(string Reason) : ApiTransactionParse;

    public sealed record InvalidPaymentType : ApiTransactionParse;
}

/// <summary>
/// Textos dos 400 do desvio 097 (o Flask respondia 500 com <c>str(e)</c> ou gravava lixo). Sem acento nas rotas de webhook,
/// como as demais mensagens delas.
/// </summary>
public static class InputMessages
{
    public const string InvalidFlow = "tipo_fluxo invalido na notificacao. Use: Renda ou Despesa";
    public const string InvalidNotificationValue = "Valor invalido na notificacao: deve ser positivo";
    public const string NoActiveAccount = "Nenhuma conta ativa encontrada. Cadastre uma conta antes de usar o atalho.";
    public const string InvalidPaymentDate = "data_pagamento invalida. Use o formato AAAA-MM-DD";
    public const string IncludeNotBoolean = "Campo 'incluir' deve ser true ou false";

    public static string InvalidValue(string reason) => "Valor invalido: " + reason;

    public static string NotText(string field) => $"Campo '{field}' deve ser texto";
}

public abstract record AmountParse
{
    public sealed record Ok(double Value) : AmountParse;

    /// <summary>Texto depois de <c>"Valor invalido: "</c>.</summary>
    public sealed record Invalid(string Reason) : AmountParse;
}

/// <summary>
/// Valor em reais de <c>/api/transacao</c> e do SMS: <c>float()</c> do Python, positivo e até 1 bilhão. O Flask deixava
/// <c>nan</c> e valor negativo do SMS passarem e gravarem lixo (desvio 097: 400).
/// </summary>
public static class PaymentAmount
{
    public const string MaxValueReason = "Valor muito alto (max: 1 bilhao)";
    public const string NotPositiveReason = "Valor deve ser positivo";

    public static AmountParse Parse(JsonElement element)
    {
        var converted = PyJsonValue.ToFloat(element);
        if (converted is FloatConversion.Failed failed)
        {
            return new AmountParse.Invalid(failed.Message);
        }

        var number = ((FloatConversion.Ok)converted).Value;
        if (double.IsNaN(number) || number <= 0)
        {
            return new AmountParse.Invalid(NotPositiveReason);
        }

        return number > 1_000_000_000 ? new AmountParse.Invalid(MaxValueReason) : new AmountParse.Ok(number);
    }
}

/// <summary>Validação do corpo de <c>/api/transacao</c> na ordem do Flask (spec 011 §3.2, linhas 3 a 8).</summary>
public static class ApiTransactionInput
{
    public const string MaxValueReason = PaymentAmount.MaxValueReason;
    public const string NotPositiveReason = PaymentAmount.NotPositiveReason;

    public static ApiTransactionParse Parse(JsonElement body)
    {
        var key = body.TryGetProperty("user_api_key", out var keyElement) ? keyElement : (JsonElement?)null;
        var valueElement = body.TryGetProperty("valor", out var v) ? v : (JsonElement?)null;

        if (!TryText(body, "local", 100, false, out var local))
        {
            return new ApiTransactionParse.UnexpectedType("local");
        }

        if (!TryText(body, "descricao", 200, true, out var description))
        {
            return new ApiTransactionParse.UnexpectedType("descricao");
        }

        if (!TryText(body, "conta", 50, false, out var account))
        {
            return new ApiTransactionParse.UnexpectedType("conta");
        }

        if (!TryPaymentType(body, out var paymentType))
        {
            return new ApiTransactionParse.UnexpectedType("tipo_pagamento");
        }

        var missing = new List<string>();
        if (!PyJsonValue.IsTruthy(key))
        {
            missing.Add("user_api_key");
        }

        if (!PyJsonValue.IsTruthy(valueElement))
        {
            missing.Add("valor");
        }

        if (string.IsNullOrEmpty(local))
        {
            missing.Add("local");
        }

        if (string.IsNullOrEmpty(account))
        {
            missing.Add("conta");
        }

        if (string.IsNullOrEmpty(paymentType))
        {
            missing.Add("tipo_pagamento");
        }

        if (missing.Count > 0)
        {
            return new ApiTransactionParse.Missing(missing);
        }

        var amount = PaymentAmount.Parse(valueElement!.Value);
        if (amount is AmountParse.Invalid invalid)
        {
            return new ApiTransactionParse.InvalidValue(invalid.Reason);
        }

        var number = ((AmountParse.Ok)amount).Value;

        if (!PaymentType.IsValid(paymentType!))
        {
            return new ApiTransactionParse.InvalidPaymentType();
        }

        var key2 = key is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
        return new ApiTransactionParse.Ok(new ApiTransactionData(
            key2, number, local!, account!, paymentType!, string.IsNullOrEmpty(description) ? null : description));
    }

    private static bool TryText(JsonElement body, string name, int maxLength, bool allowSpecial, out string? result)
    {
        result = null;
        if (!body.TryGetProperty(name, out var element) || !PyJsonValue.IsTruthy(element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = InputSanitizer.Sanitize(element.GetString(), maxLength, allowSpecial);
        return true;
    }

    private static bool TryPaymentType(JsonElement body, out string? result)
    {
        result = null;
        if (!TryText(body, "tipo_pagamento", 20, false, out var sanitized))
        {
            return false;
        }

        result = string.IsNullOrEmpty(sanitized) ? null : PaymentType.Normalize(sanitized);
        return true;
    }
}

/// <summary>Resultado da extração do Gemini para uma notificação (<c>handle_automate</c> passo 2).</summary>
public sealed record NotificationExtraction(string FlowType, string? Description, double Value)
{
    public bool IsIncome => FlowType == "Renda";
}

public abstract record NotificationParse
{
    public sealed record Ok(NotificationExtraction Extraction) : NotificationParse;

    /// <summary><c>tipo_fluxo</c> que não é <c>Renda</c> nem <c>Despesa</c> (desvio 097: 400).</summary>
    public sealed record InvalidFlow : NotificationParse;

    /// <summary><c>valor_decimal</c> ausente, nulo, não numérico, zero, negativo ou fora da faixa (desvio 097: 400).</summary>
    public sealed record InvalidValue : NotificationParse;

    /// <summary>Resposta ilegível (JSON inválido, não objeto, <c>descricao_bruta</c> que não é texto): falha do Gemini, 500.</summary>
    public sealed record Unreadable : NotificationParse;
}

public static class NotificationParser
{
    /// <summary>
    /// <c>json.loads</c> e <c>get('tipo_fluxo','Despesa')</c>, <c>get('descricao_bruta')</c>, <c>float(get('valor_decimal',0))</c>.
    /// O Flask gravava pendência de R$ 0,00 para valor ausente ou zero e quebrava com 500 para os demais casos inválidos;
    /// desvio 097: tipo de fluxo e valor inválidos viram 400.
    /// </summary>
    public static NotificationParse Parse(string geminiText)
    {
        JsonElement root;
        try
        {
            root = LlmJson.Parse(geminiText);
        }
        catch (JsonException)
        {
            return new NotificationParse.Unreadable();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return new NotificationParse.Unreadable();
        }

        var flow = "Despesa";
        if (root.TryGetProperty("tipo_fluxo", out var flowElement))
        {
            if (flowElement.ValueKind != JsonValueKind.String)
            {
                return new NotificationParse.InvalidFlow();
            }

            flow = flowElement.GetString()!;
        }

        if (flow is not ("Renda" or "Despesa"))
        {
            return new NotificationParse.InvalidFlow();
        }

        string? description = null;
        if (root.TryGetProperty("descricao_bruta", out var descriptionElement) && descriptionElement.ValueKind != JsonValueKind.Null)
        {
            if (descriptionElement.ValueKind != JsonValueKind.String)
            {
                return new NotificationParse.Unreadable();
            }

            description = descriptionElement.GetString();
        }

        if (!root.TryGetProperty("valor_decimal", out var valueElement)
            || valueElement.ValueKind == JsonValueKind.Null
            || PaymentAmount.Parse(valueElement) is not AmountParse.Ok amount)
        {
            return new NotificationParse.InvalidValue();
        }

        return new NotificationParse.Ok(new NotificationExtraction(flow, description, amount.Value));
    }
}

/// <summary>Escolha da conta e do grupo de categorias do Automate (<c>transaction_handler.py:73-82</c>).</summary>
public static class AutomateChoice
{
    private static readonly string[] CardWords = ["cartao", "compra", "credit"];

    /// <summary>
    /// Despesa com palavra de cartão no texto: o primeiro <c>Cartão de Crédito</c> (desvio 025); senão a primeira
    /// <c>Conta Corrente</c>; senão a primeira conta. <c>null</c> = nenhuma conta ativa.
    /// </summary>
    public static AccountRef? ChooseAccount(string notificationText, string flowType, IReadOnlyList<AccountRef> accounts)
    {
        if (accounts.Count == 0)
        {
            return null;
        }

        if (flowType == "Despesa")
        {
            var lower = notificationText.ToLowerInvariant();
            if (CardWords.Any(word => lower.Contains(word, StringComparison.Ordinal)))
            {
                var card = accounts.FirstOrDefault(a => a.Type == AccountTypes.Card);
                if (card is not null)
                {
                    return card;
                }
            }
        }

        return accounts.FirstOrDefault(a => a.Type == "Conta Corrente") ?? accounts[0];
    }
}

/// <summary><c>date.fromisoformat</c> do Python 3.11 para o que importa: texto <c>YYYY-MM-DD</c> ou <c>YYYYMMDD</c>.</summary>
public static partial class PyIsoDate
{
    public static bool TryParse(JsonElement element, out DateOnly date)
    {
        date = default;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var match = IsoPattern().Match(element.GetString()!);
        if (!match.Success)
        {
            return false;
        }

        var year = int.Parse(Pick(match, 1), CultureInfo.InvariantCulture);
        var month = int.Parse(Pick(match, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(Pick(match, 3), CultureInfo.InvariantCulture);
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    /// <summary>Formato estendido (<c>2026-01-15</c>) ou básico (<c>20260115</c>); misturar os dois é inválido.</summary>
    [GeneratedRegex(@"^(?:([0-9]{4})-([0-9]{2})-([0-9]{2})|([0-9]{4})([0-9]{2})([0-9]{2}))$")]
    private static partial Regex IsoPattern();

    private static string Pick(Match match, int index) =>
        match.Groups[index].Success ? match.Groups[index].Value : match.Groups[index + 3].Value;
}

/// <summary>Filtros e paginação de <c>GET /api/agendamentos/reserva</c> (<c>reserve_handler.py:217-274</c>).</summary>
public sealed record ReserveListFilter(bool? IncludeInReserve, string? Periodicity, string? Category, long Limit, long Offset);

public abstract record ReserveListParse
{
    public sealed record Ok(ReserveListFilter Filter) : ReserveListParse;

    public sealed record InvalidPeriodicity : ReserveListParse;

    /// <summary>
    /// <c>int()</c> do Python recusa, ou o PostgreSQL recusaria o <c>LIMIT</c>/<c>OFFSET</c> (negativo ou acima de <c>int64</c>);
    /// desvio 097: 400 em vez de 500. <paramref name="Field"/> é <c>limit</c> ou <c>offset</c>.
    /// </summary>
    public sealed record InvalidPaging(string Field) : ReserveListParse;
}

public static class ReserveListQuery
{
    public const string PeriodicityMessage = "Periodicidade inválida. Use: DIARIA, SEMANAL, QUINZENAL, MENSAL ou ANUAL";

    public static string PagingMessage(string field) =>
        $"Parâmetro '{field}' inválido. Use um número inteiro maior ou igual a zero";

    private static readonly string[] Periodicities = ["DIARIA", "SEMANAL", "QUINZENAL", "MENSAL", "ANUAL"];

    /// <summary>Cada parâmetro é <c>null</c> quando a chave não veio (o <c>args.get</c> do Flask distingue de vazio).</summary>
    public static ReserveListParse Parse(string? include, string? periodicity, string? category, string? limit, string? offset)
    {
        bool? includeFlag = include is null ? null : string.Equals(include, "true", StringComparison.OrdinalIgnoreCase);

        // Desvio 097: a paginação inválida (o Flask quebrava no int() ou no SQL, 500) é validada antes da periodicidade.
        if (!PyInt.TryParse(limit, 100, out var limitNumber) || limitNumber.Sign < 0 || limitNumber > long.MaxValue)
        {
            return new ReserveListParse.InvalidPaging("limit");
        }

        if (!PyInt.TryParse(offset, 0, out var offsetNumber) || offsetNumber.Sign < 0 || offsetNumber > long.MaxValue)
        {
            return new ReserveListParse.InvalidPaging("offset");
        }

        string? periodicityUpper = null;
        if (!string.IsNullOrEmpty(periodicity))
        {
            periodicityUpper = periodicity.ToUpperInvariant();
            if (Array.IndexOf(Periodicities, periodicityUpper) < 0)
            {
                return new ReserveListParse.InvalidPeriodicity();
            }
        }

        return new ReserveListParse.Ok(new ReserveListFilter(
            includeFlag, periodicityUpper, string.IsNullOrEmpty(category) ? null : category, (long)limitNumber, (long)offsetNumber));
    }

}

/// <summary><c>int(texto)</c> do Python: espaços, sinal, <c>_</c> entre dígitos e dígitos Unicode decimais.</summary>
public static partial class PyInt
{
    /// <summary>Ausente (<c>null</c>) vale <paramref name="defaultValue"/>; texto que o <c>int()</c> recusa devolve <c>false</c>.</summary>
    public static bool TryParse(string? text, long defaultValue, out BigInteger value)
    {
        value = defaultValue;
        if (text is null)
        {
            return true;
        }

        var match = IntPattern().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var digits = new System.Text.StringBuilder();
        foreach (var ch in match.Groups[2].Value)
        {
            if (ch != '_')
            {
                digits.Append((char)('0' + (int)char.GetNumericValue(ch)));
            }
        }

        var number = BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture);
        value = match.Groups[1].Value == "-" ? -number : number;
        return true;
    }

    [GeneratedRegex(@"^([+-]?)(\p{Nd}+(?:_\p{Nd}+)*)$")]
    private static partial Regex IntPattern();
}

/// <summary>Corpo do <c>PATCH .../reserva</c> (<c>reserve_handler.py:59-68</c>).</summary>
public abstract record ReserveToggleParse
{
    /// <summary><c>incluir</c> nulo/ausente ou <c>api_key</c> falsa (400).</summary>
    public sealed record MissingFields : ReserveToggleParse;

    /// <param name="ApiKey">Nulo quando a chave verdadeira não é texto (nunca autentica).</param>
    /// <param name="Include">Nulo quando <c>incluir</c> não é booleano (desvio 097: 400 depois da autenticação).</param>
    public sealed record Ok(string? ApiKey, bool? Include) : ReserveToggleParse;
}

public static class ReserveToggleInput
{
    public static ReserveToggleParse Parse(JsonElement? body)
    {
        if (body is not { } root)
        {
            return new ReserveToggleParse.MissingFields();
        }

        var hasInclude = root.TryGetProperty("incluir", out var include) && include.ValueKind != JsonValueKind.Null;
        var key = root.TryGetProperty("api_key", out var keyElement) ? keyElement : (JsonElement?)null;
        if (!hasInclude || !PyJsonValue.IsTruthy(key))
        {
            return new ReserveToggleParse.MissingFields();
        }

        bool? includeFlag = include.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

        return new ReserveToggleParse.Ok(key is { ValueKind: JsonValueKind.String } text ? text.GetString() : null, includeFlag);
    }
}
