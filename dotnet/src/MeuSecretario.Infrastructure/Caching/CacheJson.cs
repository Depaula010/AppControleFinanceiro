using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeuSecretario.Infrastructure.Caching;

/// <summary>
/// JSON do Redis compatível com o <c>json.dumps</c>/<c>json.loads</c> do Python (spec 010 §4.2): nomes de propriedade
/// como estão (snake_case do dict), não ASCII escapado (<c>ensure_ascii</c>), sem indentação, e leitura tolerante de
/// número escrito como texto (<c>"3"</c> lido como 3).
/// </summary>
public static class CacheJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = null,
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = false,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Vazio, texto que não é JSON, JSON <c>null</c> ou formato incompatível com <typeparamref name="T"/> devolvem o padrão.</summary>
    public static T? Deserialize<T>(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(raw, Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>Elemento JSON independente do documento; <c>null</c> para vazio, inválido ou JSON <c>null</c>.</summary>
    public static JsonElement? ParseElement(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var element = document.RootElement.Clone();
            return element.ValueKind == JsonValueKind.Null ? null : element;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
