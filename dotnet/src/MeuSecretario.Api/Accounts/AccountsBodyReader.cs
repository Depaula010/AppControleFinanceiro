using System.Globalization;
using System.Numerics;
using System.Text.Json;
using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.Api.Accounts;

/// <summary>
/// Lê o corpo como <see cref="RawBody"/> (desvio 024): ausente, vazio, inválido ou que não seja um objeto vira
/// <c>null</c>; não exige <c>Content-Type</c>. Mantém cada valor bruto para distinguir ausente de <c>null</c>
/// (o <c>JsonBody</c> compartilhado não distingue e não é usado aqui).
/// </summary>
public static class AccountsBodyReader
{
    public static async Task<RawBody?> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var values = new Dictionary<string, RawValue>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                values[property.Name] = Convert(property.Value);   // chave repetida: vale a última, como no Python
            }
            return new RawBody(values);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RawValue Convert(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => ConvertNumber(value.GetRawText()),
        JsonValueKind.String => RawValue.Text(value.GetString()!),
        JsonValueKind.True => RawValue.Bool(true),
        JsonValueKind.False => RawValue.Bool(false),
        JsonValueKind.Null => RawValue.Null,
        _ => RawValue.Other,
    };

    private static RawValue ConvertNumber(string raw) =>
        BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            ? RawValue.Whole(integer)
            : RawValue.Fractional(double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture));
}
