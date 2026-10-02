using System.Text;

namespace MeuSecretario.Domain.Accounts;

/// <summary>Os 6 tipos aceitos (CHECK <c>contas_tipo_conta_check</c>), na ordem da mensagem de erro (RN-002-07).</summary>
public static class AccountTypes
{
    public const string Card = "Cartão de Crédito";

    public static IReadOnlyList<string> All { get; } =
        ["Conta Corrente", "Conta Poupança", "Investimento", Card, "Dinheiro", "Outro"];

    /// <summary>Comparação exata: maiúsculas, acentos e espaços internos contam.</summary>
    public static bool IsValid(string value) => All.Contains(value, StringComparer.Ordinal);
}

/// <summary>Nome da conta: <c>trim</c> e corte em 100 pontos de código, como o <c>[:100]</c> do Python (RN-002-08).</summary>
public static class AccountName
{
    public const int MaxLength = 100;

    public static string Normalize(string raw) => Truncate(raw.Trim());

    /// <summary>Corta em 100 pontos de código (não unidades UTF-16); não faz novo <c>trim</c> depois do corte.</summary>
    public static string Truncate(string value) => TruncateRunes(value, MaxLength);

    internal static string TruncateRunes(string value, int maxRunes)
    {
        var count = 0;
        var end = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (count == maxRunes)
            {
                return value[..end];
            }
            count++;
            end += rune.Utf16SequenceLength;
        }
        return value;
    }

    internal static int CountRunes(string value) => value.EnumerateRunes().Count();
}

/// <summary>Erros de validação das regras de conta (sem texto em português; o endpoint mapeia para a mensagem).</summary>
public enum AccountError
{
    DataMissing,
    NameRequired,
    TypeRequired,
    TypeInvalid,
    BalanceInvalid,
    ColorInvalid,
    IconInvalid,
    LimitInvalid,
    DaysOnlyForCard,
    ClosingDayInvalid,
    DueDayInvalid,
    CardNeedsDays,
    NameEmpty,
    NothingToUpdate,
}
