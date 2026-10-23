using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

public enum CheckinReplyKind
{
    /// <summary>Tudo pago: todos os números da sessão.</summary>
    Full,

    /// <summary>Só os números citados.</summary>
    Partial,

    /// <summary>"Não", "depois"...: encerra sem pagar.</summary>
    Defer,

    /// <summary>Números fora de 1..N: encerra a sessão com o motivo.</summary>
    Error,

    /// <summary>Nada reconhecido: encerra a sessão com a ajuda.</summary>
    Invalid,
}

/// <summary>Resposta interpretada. <c>Indices</c> só vale em <see cref="CheckinReplyKind.Full"/> e <see cref="CheckinReplyKind.Partial"/>; <c>Text</c>, nos erros.</summary>
public sealed record CheckinReply(CheckinReplyKind Kind, IReadOnlyList<int> Indices, string? Text);

/// <summary>
/// <c>NightlyCheckinService.parse_checkin_response</c> (<c>nightly_checkin_service.py</c>) com o desvio 210 (implementado):
/// as palavras são procuradas como PALAVRA inteira (o Flask procurava trecho: "assim" e "joaquim" confirmavam tudo) e o adiamento tem
/// prioridade sobre a confirmação ("ok não" adia). Emojis de confirmação continuam valendo em qualquer parte; números, como antes.
/// </summary>
public static partial class CheckinReplyParser
{
    private static readonly string[] ConfirmEmojis = ["👍", "👌", "✅", "✓", "☑"];

    private static readonly string[] ConfirmWords = ["sim", "ok", "confirmar", "tudo pago", "tudo certo", "tudo ok"];

    private static readonly string[] DeferWords = ["não", "nao", "depois", "ainda não", "ainda nao", "amanhã", "amanha"];

    public const string Unrecognized =
        "❓ Não entendi. Responda com:\n\n✅ Emoji ou \"Ok\" para tudo\n📝 Números das contas pagas (ex: \"1, 3\")\n⏸️ \"Não\" para adiar";

    public static CheckinReply Parse(string message, int totalItems)
    {
        ArgumentNullException.ThrowIfNull(message);
        var lower = message.Trim().ToLowerInvariant();

        // Desvio 210: adiamento primeiro; depois a confirmação, ambos por palavra inteira.
        if (DeferWords.Any(w => ContainsWord(lower, w)))
        {
            return new CheckinReply(CheckinReplyKind.Defer, [], null);
        }

        if (ConfirmEmojis.Any(e => message.Contains(e, StringComparison.Ordinal))
            || ConfirmWords.Any(w => ContainsWord(lower, w)))
        {
            return new CheckinReply(CheckinReplyKind.Full, [.. Enumerable.Range(1, Math.Max(totalItems, 0))], null);
        }

        var numbers = Numbers(message);
        if (numbers.Count > 0)
        {
            var invalid = numbers.Where(n => n < 1 || n > totalItems).ToList();
            if (invalid.Count > 0)
            {
                var list = string.Join(", ", invalid.Select(n => n.ToString(CultureInfo.InvariantCulture)));
                return new CheckinReply(CheckinReplyKind.Error, [], $"Números inválidos: [{list}]. Use 1 a {totalItems.ToString(CultureInfo.InvariantCulture)}");
            }

            // list(set(...)) do Python: para índices pequenos a ordem do conjunto é a crescente.
            return new CheckinReply(CheckinReplyKind.Partial, [.. numbers.Select(n => (int)n).Distinct().Order()], null);
        }

        return new CheckinReply(CheckinReplyKind.Invalid, [], Unrecognized);
    }

    /// <summary>
    /// A palavra (ou frase) <paramref name="word"/> como palavra inteira: não pode ter letra, dígito ou <c>_</c> colado antes nem depois
    /// (o <c>\b</c> do <c>re</c>). Desvio 210.
    /// </summary>
    internal static bool ContainsWord(string text, string word)
    {
        var from = 0;
        while (from <= text.Length - word.Length)
        {
            var index = text.IndexOf(word, from, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            var before = index == 0 || !IsWordChar(text[index - 1]);
            var end = index + word.Length;
            var after = end >= text.Length || !IsWordChar(text[end]);
            if (before && after)
            {
                return true;
            }

            from = index + 1;
        }

        return false;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary><c>re.findall(r'\d+')</c> seguido de <c>int()</c>: dígitos Unicode contam e o número não tem limite.</summary>
    private static List<BigInteger> Numbers(string message) =>
        [.. DigitRun().Matches(message).Select(m => ToNumber(m.Value))];

    private static BigInteger ToNumber(string digits)
    {
        var ascii = new StringBuilder(digits.Length);
        foreach (var c in digits)
        {
            ascii.Append((char)('0' + CharUnicodeInfo.GetDecimalDigitValue(c)));
        }

        return BigInteger.Parse(ascii.ToString(), CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitRun();
}
