using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// Leitura do JSON que o Gemini devolve nas extrações da 012e/012f/012h, com a semântica do Python:
/// <c>dict.get(chave)</c> (ausente = <c>None</c>), verdade de <c>if valor</c> e <c>str(valor)</c>.
/// </summary>
internal static class GeminiFields
{
    /// <summary>Chama o Gemini (sem cache, com a chave do usuário) e interpreta a resposta como objeto JSON.</summary>
    public static async Task<JsonElement> ObjectAsync(
        ILlmClient llm, string prompt, int userId, CancellationToken cancellationToken)
    {
        var text = await llm.GenerateTextAsync(new LlmRequest(prompt, userId), cancellationToken);
        var json = LlmJson.Parse(text);

        // dict.get(...) num valor que não é dicionário quebra no Python (AttributeError).
        return json.ValueKind == JsonValueKind.Object
            ? json
            : throw new InvalidOperationException("Resposta do Gemini não é um objeto.");
    }

    /// <summary><c>dados.get(nome)</c>: <c>null</c> quando a chave não existe.</summary>
    public static JsonElement? Get(JsonElement data, string name) => data.TryGetProperty(name, out var value) ? value : null;

    /// <summary>
    /// <c>dados.get(nome, padrão)</c> lido como texto: o padrão só vale se a chave não existe; JSON <c>null</c> ou outro tipo viram
    /// <c>null</c> (nenhuma comparação com texto casa).
    /// </summary>
    public static string? StringOr(JsonElement data, string name, string defaultValue) =>
        data.TryGetProperty(name, out var value) ? StringOnly(value) : defaultValue;

    public static bool Truthy(JsonElement? value) => PyJsonValue.IsTruthy(value);

    /// <summary>O texto quando é texto; <c>null</c> quando é <c>null</c> ou ausente; <c>str(valor)</c> nos demais tipos.</summary>
    public static string? Text(JsonElement? value) => value is not { } element || element.ValueKind == JsonValueKind.Null
        ? null
        : element.ValueKind == JsonValueKind.String ? element.GetString() : PyJsonValue.Str(element);

    /// <summary>Só o texto vale como texto; outros tipos (número, lista) não casam com comparações como <c>== 'hoje'</c>.</summary>
    public static string? StringOnly(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;
}
