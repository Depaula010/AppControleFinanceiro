using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Settings;

namespace MeuSecretario.Api.Settings;

/// <summary>
/// Lê o corpo como <see cref="RawBody"/> (desvio 024): ausente, vazio, inválido ou que não seja um objeto vira <c>null</c>;
/// não exige <c>Content-Type</c>. Objetos aninhados viram <see cref="ObjectValue"/> e listas <see cref="ListValue"/>.
/// </summary>
public static class SettingsBodyReader
{
    public static async Task<RawBody?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ReadObject(document.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RawBody ReadObject(JsonElement element)
    {
        var values = new Dictionary<string, RawValue>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = Convert(property.Value);   // chave repetida: vale a última, como no Python
        }
        return new RawBody(values);
    }

    private static RawValue Convert(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => ConvertNumber(value.GetRawText()),
        JsonValueKind.String => RawValue.Text(value.GetString()!),
        JsonValueKind.True => RawValue.Bool(true),
        JsonValueKind.False => RawValue.Bool(false),
        JsonValueKind.Null => RawValue.Null,
        JsonValueKind.Object => new ObjectValue(ReadObject(value)),
        JsonValueKind.Array => new ListValue(value.GetArrayLength()),
        _ => RawValue.Other,
    };

    private static RawValue ConvertNumber(string raw) =>
        BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            ? RawValue.Whole(integer)
            : RawValue.Fractional(double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture));
}
