using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.ApiKeys;
using MeuSecretario.Domain.Llm;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// Chamada ao Gemini com chave por chamada e cache opcional (spec 010 §6.3). A chave é resolvida <b>antes</b> do cache
/// (RN-010-23: o uso mensal do 008 conta mesmo em HIT). Erros de cache são engolidos e contados; erro do Gemini é
/// relançado; resposta bloqueada ou falha nunca é gravada. Nada de prompt, resposta ou chave em log.
/// </summary>
public sealed class LlmClient(
    IProviderKeySelector keys,
    IGeminiTransport transport,
    ICache cache,
    LlmCacheMetricsCollector metrics,
    IClock clock,
    ILogger<LlmClient> logger) : ILlmClient
{
    private const string NotConfiguredMessage = "Modelo Gemini não configurado.";

    public async Task<string> GenerateTextAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Prompt);

        var selected = await keys.SelectAsync(request.UserId, ApiProviders.Gemini, cancellationToken).ConfigureAwait(false);
        if (selected.Failure is { } failure)
        {
            throw new LlmKeyUnavailableException(failure.Message);
        }

        if (!selected.HasKey)
        {
            throw new LlmException(NotConfiguredMessage);
        }

        var policy = request.Cache;
        if (policy is null)
        {
            return await CallAsync(request, selected.Key!, cancellationToken).ConfigureAwait(false);
        }

        if (policy.UserSpecific && request.UserId is not > 0)
        {
            throw new ArgumentException("user_specific cache requires usuario_id", nameof(request));
        }

        if (!await cache.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            LlmLog.CacheBypassed(logger, policy.Prefix);
            return await CallAsync(request, selected.Key!, cancellationToken).ConfigureAwait(false);
        }

        var started = Stopwatch.GetTimestamp();
        var cacheKey = LlmCacheKey.Build(policy.Prefix, request.UserId, policy.UserSpecific, request.Prompt);

        if (await TryReadAsync(cacheKey, policy.Prefix, cancellationToken).ConfigureAwait(false) is { } cached)
        {
            metrics.RecordHit(policy.Prefix);
            var hitMs = ElapsedMs(started);
            LlmLog.CacheHit(logger, policy.Prefix, hitMs);
            return cached;
        }

        string text;
        try
        {
            text = await CallAsync(request, selected.Key!, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.RecordError();
            throw;
        }

        metrics.RecordMiss(policy.Prefix);
        var missMs = ElapsedMs(started);
        LlmLog.CacheMiss(logger, policy.Prefix, missMs);
        await TrySaveAsync(cacheKey, policy, text, cancellationToken).ConfigureAwait(false);
        return text;
    }

    private async Task<string> CallAsync(LlmRequest request, string apiKey, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var text = await transport.GenerateAsync(request.Prompt, apiKey, cancellationToken).ConfigureAwait(false);
            var calledMs = ElapsedMs(started);
            LlmLog.Called(logger, calledMs, request.UserId);
            return text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failedMs = ElapsedMs(started);
            LlmLog.Failed(logger, failedMs, request.UserId, ex.GetType().Name);
            throw;
        }
    }

    private async Task<string?> TryReadAsync(string cacheKey, string prefix, CancellationToken cancellationToken)
    {
        try
        {
            var element = await cache.GetJsonElementAsync(cacheKey, cancellationToken).ConfigureAwait(false);
            if (element is not { ValueKind: JsonValueKind.Object } entry)
            {
                return null;
            }

            if (entry.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                return text.GetString();
            }

            // Entrada sem "text": o Python levantava KeyError, contava erro e seguia como MISS.
            metrics.RecordError();
            LlmLog.CacheError(logger, "leitura", prefix, "EntradaInvalida");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.RecordError();
            LlmLog.CacheError(logger, "leitura", prefix, ex.GetType().Name);
            return null;
        }
    }

    private async Task TrySaveAsync(string cacheKey, LlmCachePolicy policy, string text, CancellationToken cancellationToken)
    {
        try
        {
            // Mesmo formato do Python: {text, prompt_feedback, cached_at}; cached_at = datetime.now().isoformat() (São Paulo).
            var value = new Dictionary<string, string>
            {
                ["text"] = text,
                ["prompt_feedback"] = string.Empty,
                ["cached_at"] = IsoFormat(clock.Now),
            };
            if (await cache.SetJsonAsync(cacheKey, value, policy.Ttl, cancellationToken).ConfigureAwait(false))
            {
                metrics.RecordSave();
                LlmLog.CacheSaved(logger, policy.Prefix, (long)policy.Ttl.TotalSeconds);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.RecordError();
            LlmLog.CacheError(logger, "gravacao", policy.Prefix, ex.GetType().Name);
        }
    }

    private static long ElapsedMs(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    /// <summary>Equivalente ao <c>isoformat()</c> do Python (microssegundos só quando diferentes de zero).</summary>
    private static string IsoFormat(DateTime value)
    {
        var micros = value.Ticks / 10 % 1_000_000;
        var format = micros == 0 ? "yyyy-MM-dd'T'HH:mm:ss" : "yyyy-MM-dd'T'HH:mm:ss.ffffff";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }
}
