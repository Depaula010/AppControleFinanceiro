using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Polly.Timeout;

namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// <c>POST .../models/{modelo}:generateContent</c> (spec 010 §6.2). A chave vai <b>só</b> no header
/// <c>x-goog-api-key</c> desta requisição (nunca em <c>DefaultRequestHeaders</c>), então chamadas concorrentes com
/// chaves diferentes não se misturam. Timeouts e retry vêm do pipeline do <c>HttpClient</c> (<c>AddGeminiClient</c>).
/// </summary>
public sealed class GeminiHttpTransport(
    IHttpClientFactory httpClients,
    IIntegrationSettings settings,
    ILogger<GeminiHttpTransport> logger) : IGeminiTransport
{
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<string> GenerateAsync(string prompt, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentException.ThrowIfNullOrEmpty(apiKey);

        var model = string.IsNullOrWhiteSpace(settings.GeminiModel) ? GeminiHttpOptions.DefaultModel : settings.GeminiModel.Trim();
        var url = $"{GeminiHttpOptions.BaseUrl}models/{Uri.EscapeDataString(model)}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(Payload(prompt)) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("x-goog-api-key", apiKey);

        try
        {
            using var client = httpClients.CreateClient(GeminiHttpOptions.ClientName);
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LlmLog.HttpStatus(logger, (int)response.StatusCode);
            }

            return GeminiResponseParser.Parse(response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Mensagem da exceção interna pode trazer a URL; no log só o tipo.
            LlmLog.Transport(logger, ex.GetType().Name);
            throw new LlmException("Falha de comunicação com o Gemini (rede ou tempo esgotado).", ex);
        }
    }

    /// <summary><c>{"contents":[{"role":"user","parts":[{"text":prompt}]}]}</c>, sem mais nenhum campo.</summary>
    private static byte[] Payload(string prompt)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("contents");
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteStartArray("parts");
            writer.WriteStartObject();
            writer.WriteString("text", prompt);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }
}
