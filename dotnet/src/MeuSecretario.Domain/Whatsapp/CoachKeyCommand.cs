using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// Spec 016 (Q-016-1): o pedido "chave do coach" pelo WhatsApp. Só as frases exatas (sem acento e sem diferenciar maiúsculas), como o menu de ajuda,
/// para uma frase comum de finanças nunca gerar chave sem querer. Funciona sem o Gemini.
/// </summary>
public static class CoachKeyCommand
{
    private static readonly HashSet<string> Phrases = new(StringComparer.Ordinal)
    {
        "chave do coach", "chave coach", "chave da agenda", "chave da agenda do coach", "gerar chave do coach", "gerar chave da agenda", "nova chave do coach",
    };

    public static bool IsKeyRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var decomposed = message.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(c);
            }
        }

        return Phrases.Contains(string.Join(' ', plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', '!', '?'));
    }
}
