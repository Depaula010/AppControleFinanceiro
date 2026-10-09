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
/// <c>NightlyCheckinService.parse_checkin_response</c> (<c>nightly_checkin_service.py:430-480</c>), porte literal: as palavras são
/// procuradas como trecho do texto (por isso "assim" confirma tudo, pois contém "sim") e a confirmação vem antes do adiamento.
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

        if (ConfirmEmojis.Any(e => message.Contains(e, StringComparison.Ordinal))
            || ConfirmWords.Any(w => lower.Contains(w, StringComparison.Ordinal)))
        {
            return new CheckinReply(CheckinReplyKind.Full, [.. Enumerable.Range(1, Math.Max(totalItems, 0))], null);
        }

        if (DeferWords.Any(w => lower.Contains(w, StringComparison.Ordinal)))
        {
            return new CheckinReply(CheckinReplyKind.Defer, [], null);
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

/// <summary>Textos e datas da resposta ao check-in (<c>process_response</c> e <c>mark_bills_as_paid</c>).</summary>
public static class CheckinReplyTexts
{
    public const string Expired = "⏱️ Esta sessão expirou. Aguarde o próximo check-in ou registre manualmente.";

    public const string NoRecentItems = "Não há itens recentes para confirmar. Use 'Pendencias' para ver contas antigas.";

    public const string Deferred =
        "⏸️ Ok, te lembro amanhã no próximo check-in!\n\nVocê pode registrar os pagamentos manualmente a qualquer momento.";

    public const string NoValidItems = "Nenhum item válido para confirmar.";

    /// <summary>Sem o motivo da exceção (desvio 008): o Flask escreve <c>str(e)</c> depois dos dois-pontos.</summary>
    public const string SaveFailed = "❌ Erro ao salvar transações.";

    public static string Confirmed(IReadOnlyList<string> descriptions, int totalItems)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        var sb = new StringBuilder(descriptions.Count == totalItems
            ? string.Create(CultureInfo.InvariantCulture, $"✅ *Todas as {totalItems} contas confirmadas!*\n\n")
            : string.Create(CultureInfo.InvariantCulture, $"✅ *{descriptions.Count} conta(s) confirmada(s):*\n\n"));
        foreach (var description in descriptions)
        {
            sb.Append("• ").Append(description).Append('\n');
        }

        return sb.Append("\n💚 Transações registradas com sucesso!").ToString();
    }

    /// <summary>
    /// Data do lançamento (<c>mark_bills_as_paid</c>): com <c>data_vencimento_real</c> (texto ISO) vale o vencimento se o atraso é de
    /// até um dia (ou futuro), senão hoje; sem ela, o <c>dia_execucao</c> deste mês quando venceu hoje ou ontem, senão hoje.
    /// Texto que não é data ISO lança <see cref="FormatException"/>, como o <c>ValueError</c> do Python.
    /// </summary>
    public static DateOnly PaymentDate(string? realDueDate, int scheduleDay, DateOnly today)
    {
        if (!string.IsNullOrEmpty(realDueDate))
        {
            var due = DateOnly.ParseExact(realDueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            return today.DayNumber - due.DayNumber <= 1 ? due : today;
        }

        var late = today.Day - scheduleDay;
        return late is >= 0 and <= 1 ? new DateOnly(today.Year, today.Month, scheduleDay) : today;
    }
}
