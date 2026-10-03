namespace MeuSecretario.Domain.Common;

/// <summary>
/// Divide um texto longo em partes que o bot do WhatsApp aceita (até 4096 caracteres; desvio 059). Prefere cortar na
/// última quebra de linha da janela; sem quebra, corta no limite sem separar um par substituto (emoji).
/// </summary>
public static class MessageChunker
{
    public const int MaxLength = 4096;

    public static IReadOnlyList<string> Split(string text, int maxLength = MaxLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);

        if (text.Length <= maxLength)
        {
            return [text];
        }

        var parts = new List<string>();
        var start = 0;
        while (text.Length - start > maxLength)
        {
            var window = text.AsSpan(start, maxLength);
            var newline = window.LastIndexOf('\n');
            if (newline > 0)
            {
                // A quebra de linha vira o separador entre as mensagens e não vai em nenhuma delas.
                parts.Add(text.Substring(start, newline));
                start += newline + 1;
                continue;
            }

            var length = maxLength;
            if (char.IsHighSurrogate(text[start + length - 1]))
            {
                length--;
            }

            parts.Add(text.Substring(start, length));
            start += length;
        }

        if (start < text.Length)
        {
            parts.Add(text[start..]);
        }

        return [.. parts.Where(p => p.Length > 0)];
    }
}
