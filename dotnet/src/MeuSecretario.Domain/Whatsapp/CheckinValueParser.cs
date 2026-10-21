using System.Globalization;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

public enum CheckinValueKind
{
    /// <summary>Um valor em reais ("150", "R$ 1.234,56").</summary>
    Amount,

    /// <summary>"Ok": vale o valor previsto do agendamento.</summary>
    UseExpected,

    /// <summary>"Pular", "não": não lança esta conta agora.</summary>
    Skip,

    /// <summary>Nada reconhecido: a pergunta se repete.</summary>
    Invalid,
}

public sealed record CheckinValueReply(CheckinValueKind Kind, decimal? Amount);

/// <summary>
/// Resposta à pergunta do valor de um lembrete variável no check-in ("Qual o valor de Conta de Água?"). Aceita vírgula ou ponto
/// decimal e separador de milhar ("1.234,56", "1,234.56", "150,5", "150"). Adiar tem prioridade sobre confirmar, como em
/// <see cref="CheckinReplyParser"/>.
/// </summary>
public static partial class CheckinValueParser
{
    public const decimal MaxAmount = 99_999_999.99m;

    private static readonly string[] SkipWords = ["pular", "pula", "depois", "não", "nao", "cancelar", "amanhã", "amanha"];

    private static readonly string[] ConfirmEmojis = ["👍", "👌", "✅", "✓", "☑"];

    private static readonly string[] ConfirmWords = ["ok", "sim", "previsto", "confirmar", "isso"];

    public static CheckinValueReply Parse(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var lower = message.Trim().ToLowerInvariant();

        if (SkipWords.Any(w => CheckinReplyParser.ContainsWord(lower, w)))
        {
            return new CheckinValueReply(CheckinValueKind.Skip, null);
        }

        var match = Money().Match(lower);
        if (match.Success && TryAmount(match.Value, out var amount))
        {
            return new CheckinValueReply(CheckinValueKind.Amount, amount);
        }

        if (ConfirmEmojis.Any(e => message.Contains(e, StringComparison.Ordinal)) || ConfirmWords.Any(w => CheckinReplyParser.ContainsWord(lower, w)))
        {
            return new CheckinValueReply(CheckinValueKind.UseExpected, null);
        }

        return new CheckinValueReply(CheckinValueKind.Invalid, null);
    }

    private static bool TryAmount(string text, out decimal amount)
    {
        amount = 0;
        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');
        var decimalAt = Math.Max(lastComma, lastDot);

        // Um único separador só é decimal se vier seguido de 1 ou 2 dígitos ("150,5", "44.27"); "1.234" ou "1,234" são milhares.
        if (lastComma >= 0 && lastDot >= 0)
        {
            // Os dois: o último é o decimal.
        }
        else if (decimalAt >= 0)
        {
            var digitsAfter = text.Length - decimalAt - 1;
            var separators = text.Count(c => c is ',' or '.');
            if (digitsAfter == 3 || separators > 1)
            {
                decimalAt = -1;
            }
        }

        var integer = decimalAt < 0 ? text : text[..decimalAt];
        var fraction = decimalAt < 0 ? string.Empty : text[(decimalAt + 1)..];
        var digits = new string([.. integer.Where(char.IsAsciiDigit)]);
        var fractionDigits = new string([.. fraction.Where(char.IsAsciiDigit)]);
        if (digits.Length == 0 || fractionDigits.Length > 2)
        {
            return false;
        }

        var normalized = fractionDigits.Length == 0 ? digits : $"{digits}.{fractionDigits}";
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0 && amount <= MaxAmount;
    }

    [GeneratedRegex(@"\d[\d.,]*")]
    private static partial Regex Money();
}
