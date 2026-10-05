using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Automation;

/// <summary>
/// Semântica do Python sobre um valor JSON (spec 011 §7.1): verdade (<c>not x</c>), nome do tipo e <c>float()</c> com as
/// mensagens exatas de <c>ValueError</c>/<c>TypeError</c>, que <c>/api/transacao</c> devolve ao cliente.
/// </summary>
public static partial class PyJsonValue
{
    /// <summary><c>bool(x)</c> do Python; ausente (<c>null</c> aqui) e JSON <c>null</c> são falsos.</summary>
    public static bool IsTruthy(JsonElement? element)
    {
        if (element is not { } e)
        {
            return false;
        }

        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => e.GetString()!.Length > 0,
            JsonValueKind.Number => NumberIsNonZero(e),
            JsonValueKind.Array => e.GetArrayLength() > 0,
            JsonValueKind.Object => e.EnumerateObject().Any(),
            _ => false,
        };
    }

    /// <summary>Nome do tipo Python (<c>type(x).__name__</c>) para as mensagens de <c>TypeError</c>.</summary>
    public static string TypeName(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => "list",
        JsonValueKind.Object => "dict",
        JsonValueKind.String => "str",
        JsonValueKind.True or JsonValueKind.False => "bool",
        JsonValueKind.Number => element.GetRawText().AsSpan().IndexOfAny('.', 'e', 'E') >= 0 ? "float" : "int",
        _ => "NoneType",
    };

    /// <summary><c>float(valor)</c>: número, booleano e texto numérico convertem; o resto falha com a mensagem do Python 3.11.</summary>
    public static FloatConversion ToFloat(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                return new FloatConversion.Ok(1);
            case JsonValueKind.False:
                return new FloatConversion.Ok(0);
            case JsonValueKind.Number:
                return new FloatConversion.Ok(double.Parse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture));
            case JsonValueKind.String:
                var text = element.GetString()!;
                // Texto só de espaços: o CPython mostra o texto já sem espaços (vazio).
                return TryParseFloat(text, out var parsed)
                    ? new FloatConversion.Ok(parsed)
                    : new FloatConversion.Failed("could not convert string to float: " + Repr(text.Trim().Length == 0 ? string.Empty : text));
            default:
                return new FloatConversion.Failed(
                    "float() argument must be a string or a real number, not '" + TypeName(element) + "'");
        }
    }

    /// <summary>
    /// <c>float(str)</c> do Python: espaços nas pontas, sinal, <c>_</c> entre dígitos, expoente, <c>inf</c>/<c>infinity</c>/<c>nan</c>
    /// (sem diferenciar caixa) e dígitos Unicode decimais.
    /// </summary>
    public static bool TryParseFloat(string text, out double value)
    {
        value = 0;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var sign = 1.0;
        var body = trimmed;
        if (body[0] is '+' or '-')
        {
            sign = body[0] == '-' ? -1.0 : 1.0;
            body = body[1..];
        }

        switch (body.ToLowerInvariant())
        {
            case "inf" or "infinity":
                value = sign * double.PositiveInfinity;
                return true;
            case "nan":
                value = double.NaN;
                return true;
        }

        var match = NumberPattern().Match(body);
        if (!match.Success)
        {
            return false;
        }

        var ascii = new StringBuilder(body.Length);
        foreach (var ch in body)
        {
            if (ch == '_')
            {
                continue;
            }

            ascii.Append(char.IsDigit(ch) ? (char)('0' + (int)char.GetNumericValue(ch)) : ch);
        }

        if (!double.TryParse(ascii.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        value = sign * number;
        return true;
    }

    /// <summary><c>repr(str)</c> do Python: aspas simples (ou duplas se o texto tem <c>'</c> e não tem <c>"</c>) e escapes.</summary>
    public static string Repr(string text)
    {
        var quote = text.Contains('\'', StringComparison.Ordinal) && !text.Contains('"', StringComparison.Ordinal) ? '"' : '\'';
        var sb = new StringBuilder(text.Length + 2);
        sb.Append(quote);
        foreach (var rune in text.EnumerateRunes())
        {
            var code = rune.Value;
            if (code == quote || code == '\\')
            {
                sb.Append('\\').Append((char)code);
            }
            else if (code == '\t')
            {
                sb.Append("\\t");
            }
            else if (code == '\n')
            {
                sb.Append("\\n");
            }
            else if (code == '\r')
            {
                sb.Append("\\r");
            }
            else if (code < 0x20 || code == 0x7f)
            {
                sb.Append("\\x").Append(code.ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (code < 0x7f || IsPrintable(rune))
            {
                sb.Append(rune.ToString());
            }
            else if (code <= 0xff)
            {
                sb.Append("\\x").Append(code.ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (code <= 0xffff)
            {
                sb.Append("\\u").Append(code.ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append("\\U").Append(code.ToString("x8", CultureInfo.InvariantCulture));
            }
        }

        return sb.Append(quote).ToString();
    }

    private static bool IsPrintable(System.Text.Rune rune) => System.Text.Rune.GetUnicodeCategory(rune) is not
        (UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
        or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
        or UnicodeCategory.SpaceSeparator);

    private static bool NumberIsNonZero(JsonElement element) =>
        double.Parse(element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture) != 0;

    [GeneratedRegex(@"^(?:\p{Nd}+(?:_\p{Nd}+)*(?:\.(?:\p{Nd}+(?:_\p{Nd}+)*)?)?|\.\p{Nd}+(?:_\p{Nd}+)*)(?:[eE][+-]?\p{Nd}+(?:_\p{Nd}+)*)?$")]
    private static partial Regex NumberPattern();
}

/// <summary>Resultado de <c>float(x)</c>: o número ou o texto do erro do Python.</summary>
public abstract record FloatConversion
{
    public sealed record Ok(double Value) : FloatConversion;

    public sealed record Failed(string Message) : FloatConversion;
}
