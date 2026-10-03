using System.Text.Json;

namespace MeuSecretario.Domain.Llm;

/// <summary>Leitura do JSON devolvido pelo modelo (RN-010-25).</summary>
public static class LlmJson
{
    /// <summary>
    /// Como <c>text.strip().replace("```json","").replace("```","")</c> seguido de <c>json.loads</c>.
    /// </summary>
    /// <exception cref="JsonException">Texto que não é JSON.</exception>
    public static JsonElement Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var cleaned = text.Trim()
            .Replace("```json", string.Empty, StringComparison.Ordinal)
            .Replace("```", string.Empty, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(cleaned);
        return document.RootElement.Clone();
    }
}
