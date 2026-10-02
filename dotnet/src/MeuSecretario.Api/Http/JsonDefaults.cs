using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MeuSecretario.Api.Http;

/// <summary>
/// Serialização compatível com o Flask: nomes de propriedade exatamente como escritos
/// (sem camelCase), valores nulos preservados, acentos e emojis sem escape.
/// </summary>
public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = Apply(new JsonSerializerOptions());

    public static JsonSerializerOptions Apply(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = null;
        options.DictionaryKeyPolicy = null;
        options.PropertyNameCaseInsensitive = true;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.WriteIndented = false;
        return options;
    }
}
