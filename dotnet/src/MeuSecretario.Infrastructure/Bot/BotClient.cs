using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Domain.Common;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Bot;

/// <summary>
/// Envio de texto e imagem pelo bot do WhatsApp (spec 010 §5). Com sessão usa a API v1 (<c>X-API-Key</c>); sem sessão,
/// o endpoint legado (<c>x-api-key = API_SECRET_KEY</c>). 404 sem <c>BOT_SESSION_ID</c> apaga o cache, resolve de novo e
/// repete o POST uma única vez. Sucesso é HTTP 200 exato; falha de rede nunca lança nem repete.
/// </summary>
public sealed class BotClient(
    IHttpClientFactory httpClients,
    BotSessionResolver sessions,
    ICache cache,
    IIntegrationSettings settings,
    ILogger<BotClient> logger) : IBotClient
{
    private const string TextKind = "texto";
    private const string ImageKind = "imagem";

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public async Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken)
    {
        // Desvio 059: o bot recusa mais de 4096 caracteres; divide em quebra de linha e envia em sequência.
        var parts = MessageChunker.Split(message);
        for (var i = 0; i < parts.Count; i++)
        {
            var payload = Payload(writer =>
            {
                writer.WriteString("numero", number);
                writer.WriteString("mensagem", parts[i]);
            });

            if (!await SendAsync(TextKind, BotHttpOptions.TextClient, "send-message", "enviar-mensagem", number, payload, cancellationToken))
            {
                if (parts.Count > 1)
                {
                    BotLog.ChunkFailed(logger, TextKind, i + 1, parts.Count, PhoneMask.Last4(number));
                }

                return false;
            }
        }

        return true;
    }

    public Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken)
    {
        var payload = Payload(writer =>
        {
            writer.WriteString("numero", number);
            writer.WriteBase64String("imagem", image.Span);
            if (caption is null)
            {
                writer.WriteNull("legenda");
            }
            else
            {
                writer.WriteString("legenda", caption);
            }
        });

        return SendAsync(ImageKind, BotHttpOptions.ImageClient, "send-image", "enviar-imagem", number, payload, cancellationToken);
    }

    private static byte[] Payload(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    private async Task<bool> SendAsync(string kind, string clientName, string v1Suffix, string legacyPath, string number, byte[] payload, CancellationToken cancellationToken)
    {
        var baseUrl = settings.BotWhatsappUrl.Trim().TrimEnd('/');
        var masked = PhoneMask.Last4(number);
        if (baseUrl.Length == 0)
        {
            BotLog.NoUrl(logger, kind, masked);
            return false;
        }

        try
        {
            var sessionId = await sessions.ResolveAsync(forceRefresh: false, cancellationToken);
            var status = await PostAsync(clientName, baseUrl, sessionId, v1Suffix, legacyPath, payload, cancellationToken);

            if (status == HttpStatusCode.NotFound && string.IsNullOrWhiteSpace(settings.BotSessionId))
            {
                BotLog.SessionRetry(logger, kind);
                await cache.DeleteAsync(CacheKeys.BotSessionId(settings.BotSessionName), cancellationToken);
                sessionId = await sessions.ResolveAsync(forceRefresh: true, cancellationToken);
                status = await PostAsync(clientName, baseUrl, sessionId, v1Suffix, legacyPath, payload, cancellationToken);
            }

            if (status == HttpStatusCode.OK)
            {
                return true;
            }

            BotLog.SendRejected(logger, kind, (int)status, masked);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            BotLog.SendFailed(logger, kind, masked, ex.GetType().Name);
            return false;
        }
    }

    private async Task<HttpStatusCode> PostAsync(string clientName, string baseUrl, string? sessionId, string v1Suffix, string legacyPath, byte[] payload, CancellationToken cancellationToken)
    {
        string url;
        string headerName;
        string headerValue;
        if (sessionId is null)
        {
            url = $"{baseUrl}/{legacyPath}";
            headerName = "x-api-key";
            headerValue = settings.ApiSecretKey;
        }
        else
        {
            url = $"{baseUrl}/api/v1/sessions/{Uri.EscapeDataString(sessionId)}/{v1Suffix}";
            headerName = "X-API-Key";
            headerValue = settings.BotApiKey;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(payload) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation(headerName, headerValue);

        using var client = httpClients.CreateClient(clientName);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return response.StatusCode;
    }
}
