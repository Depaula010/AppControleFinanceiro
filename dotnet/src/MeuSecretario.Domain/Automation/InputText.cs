using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Automation;

/// <summary><c>sanitize_input</c> (<c>app/shared/validators/input_sanitizer.py:43-68</c>, spec 011 §7.2).</summary>
public static partial class InputSanitizer
{
    private const string Dangerous = "<>{}\\$`|;&";

    /// <summary>Falso (<c>null</c> ou vazio) volta como veio; senão corta, limpa controles e perigosos, colapsa espaços e faz <c>trim</c>.</summary>
    public static string? Sanitize(string? text, int maxLength, bool allowSpecialChars)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count++ >= maxLength)
            {
                break;
            }

            var code = rune.Value;
            if (code < 32 && code is not ('\t' or '\n' or '\r'))
            {
                continue;
            }

            if (!allowSpecialChars && code < 128 && Dangerous.Contains((char)code, StringComparison.Ordinal))
            {
                continue;
            }

            sb.Append(rune.ToString());
        }

        return Whitespace().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Tipos de pagamento do atalho (<c>transaction_handler.py:217-266</c>).</summary>
public static class PaymentType
{
    public const string Credit = "credito";

    private static readonly string[] Valid = ["credito", "debito", "pix", "dinheiro"];

    public static string ValidList => string.Join(", ", Valid);

    public static bool IsValid(string normalized) => Array.IndexOf(Valid, normalized) >= 0;

    /// <summary><c>strip().lower()</c>, NFKD e descarte do que não é ASCII (<c>encode('ascii','ignore')</c>).</summary>
    public static string Normalize(string sanitized)
    {
        var lower = sanitized.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (ch < 128)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }
}

/// <summary>Dinheiro como o Flask escreve (<c>formatar_moeda</c> sem locale; <c>{:,.2f}</c> do Python).</summary>
public static class MoneyText
{
    private const double DecimalSafeLimit = 1e27;

    /// <summary><c>R$ 1.234,56</c>; nulo vira <c>R$ 0,00</c>; negativo vira <c>R$ -5,00</c> (fallback manual do Flask).</summary>
    public static string Brl(double? value)
    {
        if (value is not { } v)
        {
            return "R$ 0,00";
        }

        return "R$ " + Grouped2(v).Replace(",", "X", StringComparison.Ordinal).Replace(".", ",", StringComparison.Ordinal)
            .Replace("X", ".", StringComparison.Ordinal);
    }

    /// <summary><c>f"{v:,.2f}"</c>: vírgula de milhar, ponto decimal, meio-par sobre o valor binário exato.</summary>
    public static string Grouped2(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        if (Math.Abs(value) >= DecimalSafeLimit)
        {
            return value.ToString("N2", CultureInfo.InvariantCulture);
        }

        var exact = decimal.Parse(value.ToString("F30", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        var rounded = decimal.Round(exact, 2, MidpointRounding.ToEven);
        var text = Math.Abs(rounded).ToString("N2", CultureInfo.InvariantCulture);
        return double.IsNegative(value) ? "-" + text : text;
    }
}

/// <summary>Arredondamento e texto de <c>float</c> do Python usados nas mensagens de pote.</summary>
public static class PyFloat
{
    /// <summary><c>round(x, digits)</c>: meio-par sobre o valor binário exato.</summary>
    public static double Round(double value, int digits)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= 1e27)
        {
            return value;
        }

        var exact = decimal.Parse(value.ToString("F30", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        return (double)decimal.Round(exact, digits, MidpointRounding.ToEven);
    }

    /// <summary><c>str(float)</c> para valores comuns: sempre com casa decimal (<c>96.0</c>), menor representação que volta ao valor.</summary>
    public static string Repr(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('E', StringComparison.Ordinal) || text.Contains('.', StringComparison.Ordinal) ? text : text + ".0";
    }
}

/// <summary>Valor do JSON como <see cref="decimal"/> pela representação mais curta do <see cref="double"/> (como o driver do Python envia o <c>float</c>).</summary>
public static class MoneyDecimal
{
    /// <summary>Módulo máximo (exclusivo) de <c>numeric(15,2)</c>.</summary>
    private const decimal Limit = 10_000_000_000_000m;

    public static bool TryFromDouble(double value, out decimal result)
    {
        result = 0;
        if (!double.IsFinite(value))
        {
            return false;
        }

        if (!decimal.TryParse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            return false;
        }

        return Math.Abs(Math.Round(result, 2, MidpointRounding.AwayFromZero)) < Limit;
    }
}

/// <summary><c>json.dumps</c> da lista de categorias que vai no prompt (<c>ensure_ascii</c>, separadores <c>, </c> e <c>: </c>).</summary>
public static class PythonJson
{
    public static string DumpCategories(IEnumerable<CategoryOption> categories)
    {
        var sb = new StringBuilder("[");
        var first = true;
        foreach (var category in categories)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append("{\"id\": ").Append(category.Id.ToString(CultureInfo.InvariantCulture))
                .Append(", \"nome_sub\": ").Append(Quote(category.SubName))
                .Append(", \"nome_macro\": ").Append(Quote(category.MacroName))
                .Append('}');
        }

        return sb.Append(']').ToString();
    }

    /// <summary>Texto como o <c>json.dumps</c> do Python escreve (não ASCII em <c>\uXXXX</c>, hexadecimal minúsculo).</summary>
    public static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (ch is < (char)0x20 or > (char)0x7e)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}

/// <summary>Subcategoria oferecida ao Gemini (<c>get_user_categories</c>).</summary>
public sealed record CategoryOption(int Id, string SubName, string MacroName);

/// <summary>Conta resolvida por nome ou tipo (<c>id, nome_conta, tipo_conta</c>).</summary>
public sealed record AccountRef(int Id, string Name, string Type);
