using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace MeuSecretario.Api.Http;

/// <summary>
/// Leitura tolerante do corpo JSON (desvio 024): corpo ausente, vazio, inválido ou que não seja um objeto
/// vira <c>null</c> (a rota responde 400 com a mensagem de campos obrigatórios), nunca 500.
/// Não exige <c>Content-Type: application/json</c>.
/// </summary>
public static class JsonBody
{
    public static async Task<JsonElement?> ReadObjectAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Texto da propriedade; <c>null</c> se ausente ou se o valor não for texto.</summary>
    public static string? GetText(this JsonElement? body, string name) =>
        body is { } root && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// Valor bruto da propriedade para a conversão estilo <c>int()</c>: inteiro (<see cref="BigInteger"/>),
    /// número com parte decimal (<see cref="double"/>), texto, booleano ou um marcador para objeto/lista.
    /// <c>null</c> se ausente ou nulo.
    /// </summary>
    public static object? GetRaw(this JsonElement? body, string name)
    {
        if (body is not { } root || !root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => ParseNumber(value.GetRawText()),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => new object(), // objeto ou lista: nunca é um dia válido
        };
    }

    private static object ParseNumber(string raw) =>
        BigInteger.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            ? integer
            : double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
}
