using System.Text.Json;

namespace MeuSecretario.Api.ApiKeys;

/// <summary>
/// Leitura de campos do corpo com checagem de tipo (desvio 048): campo de tipo inesperado vira 400 "Dados incompletos"
/// da rota, nunca 500. Ausente e <c>null</c> contam como ausentes (o <c>not x</c> do Python).
/// </summary>
internal static class ApiKeysBody
{
    private static bool TryGet(JsonElement? body, string name, out JsonElement value)
    {
        value = default;
        return body is { } root && root.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null;
    }

    /// <summary>Verdadeiro se o campo está ausente/nulo ou é texto.</summary>
    public static bool TextOrAbsent(this JsonElement? body, string name) =>
        !TryGet(body, name, out var value) || value.ValueKind == JsonValueKind.String;

    /// <summary>Verdadeiro se o campo está ausente/nulo ou é booleano JSON.</summary>
    public static bool BoolOrAbsent(this JsonElement? body, string name) =>
        !TryGet(body, name, out var value) || value.ValueKind is JsonValueKind.True or JsonValueKind.False;

    /// <summary>Verdadeiro se o campo está ausente/nulo ou é um inteiro JSON que cabe em <see cref="int"/>.</summary>
    public static bool IntOrAbsent(this JsonElement? body, string name) =>
        !TryGet(body, name, out var value) || (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _));

    /// <summary>Booleano do campo; <c>null</c> se ausente, nulo ou de outro tipo.</summary>
    public static bool? GetBool(this JsonElement? body, string name) =>
        TryGet(body, name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>Inteiro do campo; <c>null</c> se ausente, nulo ou de outro tipo.</summary>
    public static int? GetInt(this JsonElement? body, string name) =>
        TryGet(body, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
