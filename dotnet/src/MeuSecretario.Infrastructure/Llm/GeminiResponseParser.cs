using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// Lê a resposta do <c>generateContent</c> e aplica o mapeamento de erros do <c>safe_generate_content</c> (RN-010-24,
/// RN-010-25). Nenhuma exceção carrega corpo de resposta nem chave.
/// </summary>
public static class GeminiResponseParser
{
    public const string QuotaMessage = "429 Quota exceeded - Gemini API rate limit reached";
    public const string PermissionMessage = "403 Permission denied - Invalid API key or permissions";

    /// <summary>Texto concatenado das partes do 1º candidato ou a exceção correspondente ao status.</summary>
    public static string Parse(HttpStatusCode status, string body)
    {
        var code = (int)status;
        return code is >= 200 and < 300 ? ParseSuccess(body) : throw MapError(code, body);
    }

    private static string ParseSuccess(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new LlmException("Falha na API do Gemini: resposta inválida.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            var text = root.ValueKind == JsonValueKind.Object ? ExtractText(root) : null;
            if (!string.IsNullOrEmpty(text) && BlockReason(root) is null)
            {
                return text;
            }

            throw new LlmBlockedException(
                "Falha na API: Resposta do Gemini bloqueada. " + (BlockReason(root) ?? "sem detalhes"));
        }
    }

    private static string? BlockReason(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("promptFeedback", out var feedback)
        && feedback.ValueKind == JsonValueKind.Object
        && feedback.TryGetProperty("blockReason", out var reason)
        && reason.ValueKind == JsonValueKind.String
        && !string.IsNullOrEmpty(reason.GetString())
            ? reason.GetString()
            : null;

    private static string? ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var first = candidates[0];
        if (first.ValueKind != JsonValueKind.Object
            || !first.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.ToString();
    }

    private static LlmException MapError(int code, string body)
    {
        string? errorStatus = null;
        string? errorMessage = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                errorStatus = error.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                errorMessage = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // corpo que não é JSON: vale só o código HTTP.
        }

        if (code == 429
            || string.Equals(errorStatus, "RESOURCE_EXHAUSTED", StringComparison.Ordinal)
            || Contains(errorMessage, "quota"))
        {
            return new LlmQuotaExceededException(QuotaMessage);
        }

        if (code == 403
            || string.Equals(errorStatus, "PERMISSION_DENIED", StringComparison.Ordinal)
            || Contains(errorMessage, "permission"))
        {
            return new LlmPermissionDeniedException(PermissionMessage);
        }

        return new LlmException(string.Create(CultureInfo.InvariantCulture, $"Falha na API do Gemini (HTTP {code})."));
    }

    private static bool Contains(string? text, string value) =>
        text is not null && text.Contains(value, StringComparison.OrdinalIgnoreCase);
}
