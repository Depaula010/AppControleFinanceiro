using System.Globalization;
using System.Numerics;
using System.Text;

namespace MeuSecretario.Domain.Auth;

/// <summary>Número de WhatsApp: guardado e buscado só com dígitos (spec 001, RN-001-05; <c>auth.py</c>).</summary>
public static class PhoneNumber
{
    public static string Digits(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsAsciiDigit(c))
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }
}

/// <summary>Senha com mínimo de 6 caracteres, contados como o <c>len()</c> do Python (pontos de código).</summary>
public static class PasswordRules
{
    public const int MinLength = 6;

    public static bool IsValid(string trimmedPassword) =>
        trimmedPassword.EnumerateRunes().Count() >= MinLength;
}

public enum CardDaysOutcome
{
    Ok,
    NotInteger,
    OutOfRange,
}

/// <param name="Field">Nome do campo fora da faixa (<c>dia_vencimento</c> ou <c>dia_fechamento</c>).</param>
public sealed record CardDaysResult(CardDaysOutcome Outcome, int Vencimento = 0, int Fechamento = 0, string? Field = null);

/// <summary>
/// Dias de vencimento e fechamento do cartão (1..31), convertidos como o <c>int()</c> do Python
/// (<c>auth.py</c>): números inteiros, números com parte decimal (truncados) e textos inteiros
/// (com espaços) são aceitos. Booleanos e o resto são "não inteiro" (desvio 031).
/// As duas conversões acontecem antes da checagem de faixa; a faixa do vencimento vem primeiro.
/// </summary>
public static class CardDays
{
    public const int Min = 1;
    public const int Max = 31;

    public static CardDaysResult Parse(object? vencimento, object? fechamento)
    {
        if (!TryToInteger(vencimento, out var venc) || !TryToInteger(fechamento, out var fech))
        {
            return new CardDaysResult(CardDaysOutcome.NotInteger);
        }

        if (venc < Min || venc > Max)
        {
            return new CardDaysResult(CardDaysOutcome.OutOfRange, Field: "dia_vencimento");
        }

        if (fech < Min || fech > Max)
        {
            return new CardDaysResult(CardDaysOutcome.OutOfRange, Field: "dia_fechamento");
        }

        return new CardDaysResult(CardDaysOutcome.Ok, (int)venc, (int)fech);
    }

    private static bool TryToInteger(object? value, out BigInteger result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case BigInteger big:
                result = big;
                return true;
            case double d when double.IsFinite(d):
                result = new BigInteger(Math.Truncate(d));
                return true;
            case decimal m:
                result = new BigInteger(decimal.Truncate(m));
                return true;
            case string s:
                return BigInteger.TryParse(
                    s,
                    NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out result);
            default:
                result = BigInteger.Zero;
                return false;
        }
    }
}

public sealed record NewAccount(string NomeConta, string TipoConta, int? DiaVencimento, int? DiaFechamento);

/// <summary>As três contas criadas no cadastro (<c>auth.py</c>), com nomes e tipos literais.</summary>
public static class DefaultAccounts
{
    public static IReadOnlyList<NewAccount> For(int diaVencimento, int diaFechamento) =>
    [
        new("Carteira", "Dinheiro", null, null),
        new("Conta Corrente", "Conta Corrente", null, null),
        new("Cartão de Crédito", "Cartão de Crédito", diaVencimento, diaFechamento),
    ];
}
