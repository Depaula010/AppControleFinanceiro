using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// Pedido do menu de ajuda (desvio 265). Só a mensagem INTEIRA conta ("menu", "ajuda", "/menu", "Menu?"): "ajuda com a fatura" ou
/// "qual o menu do dia" seguem para o Gemini. Sem acento, sem maiúsculas e sem pontuação nas pontas.
/// </summary>
public static class HelpCommand
{
    private static readonly HashSet<string> Phrases = new(StringComparer.Ordinal)
    {
        "menu", "menu de ajuda", "ajuda", "help", "comandos", "lista de comandos", "o que voce faz",
    };

    public static bool IsHelpRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return Phrases.Contains(Normalize(message));
    }

    private static string Normalize(string message)
    {
        var decomposed = message.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(c);
            }
        }

        return string.Join(' ', plain.ToString().Trim('/', '?', '!', '.', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
